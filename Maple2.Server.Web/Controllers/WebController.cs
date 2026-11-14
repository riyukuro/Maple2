using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Enum = System.Enum;

namespace Maple2.Server.Web.Controllers;

[Route("")]
public class WebController : ControllerBase {

    private readonly WebStorage webStorage;
    private readonly GameStorage gameStorage;
    private readonly IMemoryCache cache;
    private readonly TableMetadataStorage tableMetadata;
    private static readonly TimeSpan RankingCacheDuration = TimeSpan.FromHours(1);

    public WebController(WebStorage webStorage, GameStorage gameStorage, IMemoryCache cache, TableMetadataStorage tableMetadata) {
        this.webStorage = webStorage;
        this.gameStorage = gameStorage;
        this.cache = cache;
        this.tableMetadata = tableMetadata;
    }

    // The ranking and info requests are small: irrq is 40 bytes and the search text (CSoapClient::RequestGameRanking,
    // soapclient.cpp:189-199: UTF-16 after an int byte length), ruq 28 bytes (soapclient.cpp:257-262). Chosen here: 64 KiB,
    // a search of 32,000 characters, far past any name the client's search box takes. Before, both routes took
    // Kestrel's default 30 MB body, read whole into memory, from anyone (no login).
    public const int SmallRequestLimit = 0x10000;

    [HttpPost("irrq.aspx")]
    [RequestSizeLimit(SmallRequestLimit)]
    public async Task<IResult> Rankings() {
        Stream bodyStream = Request.Body;
        var memoryStream = new MemoryStream();
        await bodyStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // reset position to beginning of stream before returning
        if (memoryStream.Length == 0) {
            return Results.BadRequest("Request was empty");
        }
        IByteReader packet = new ByteReader(memoryStream.ToArray());
        var request = GameRankingRequest.Read(packet);
        (GameRankingType type, int unknown, long accountId, long characterId, long characterIdSearch, int rankingId, int seasonId, string userName) = request;

        // No key: an empty answer, as for a board with nothing to serve. A 400 makes the client send the request
        // twice more, 500 ms apart.
        if (request.KeyNotBuilt) {
            Log.Logger.Information("Ranking request without a board key (type 0): the played client's season table has no season at the server time for this board");
            return Results.File(ZlibCompress(new ByteWriter()), "application/octet-stream");
        }

        if (!Enum.IsDefined(typeof(GameRankingType), type)) {
            Log.Logger.Debug("Unimplemented ranking type: {Type}", type);
            return Results.BadRequest($"Invalid ranking type: {type}");
        }

        Log.Logger.Debug("[Request]Rankings: type={Type}, unknown={unknown} accountId={AccountId} characterId={CharacterId}, characterIdSearch={characterIdSearch}, rankingId={RankingId}, seasonId={seasonId}, userName={UserName}", type, unknown, accountId, characterId, characterIdSearch, rankingId, seasonId, userName);

        ByteWriter pWriter = type switch {
            GameRankingType.Trophy => Trophy(userName),
            GameRankingType.PersonalTrophy => PersonalTrophy(characterId),
            GameRankingType.GuildTrophy => GuildTrophy(userName),
            GameRankingType.PersonalGuildTrophy => PersonalGuildTrophy(characterId),
            GameRankingType.DarkDescent or GameRankingType.DarkDescentPreviousSeason => DarkStream(type, rankingId, userName),
            GameRankingType.DarkDescentPersonal or GameRankingType.DarkDescentPersonalPreviousSeason => PersonalDarkStream(type, rankingId, characterId),
            GameRankingType.BossClearCount or GameRankingType.BossFirstClears or GameRankingType.BossShortestClears => Boss(type, rankingId, userName),
            GameRankingType.BossClearCountPersonal => PersonalBoss(rankingId, characterId),
            GameRankingType.WorldBossClearCount or GameRankingType.WorldBossFirstClears or GameRankingType.WorldBossDamage => WorldBoss(type, rankingId, userName),
            GameRankingType.WorldBossClearCountPersonal or GameRankingType.WorldBossDamagePersonal => PersonalWorldBoss(type, rankingId, characterId),
            GameRankingType.FameChallengeClearCount or GameRankingType.FameChallengeFirstClears or GameRankingType.FameChallengeShortestClears => FameChallenge(type, rankingId, userName),
            GameRankingType.FameChallengeClearCountPersonal or GameRankingType.FameChallengeShortestClearPersonal => PersonalFameChallenge(type, rankingId, characterId),
            GameRankingType.Colosseum or GameRankingType.ColosseumPreviousSeason => Colosseum(type, seasonId, rankingId, userName),
            GameRankingType.ColosseumPersonal or GameRankingType.ColosseumPersonalPreviousSeason => PersonalColosseum(type, seasonId, rankingId, characterId),
            GameRankingType.GuildPvp or GameRankingType.GuildPvpPreviousSeason => GuildPvpBoard(type, seasonId, userName),
            GameRankingType.GuildPvpPersonal or GameRankingType.GuildPvpPersonalPreviousSeason => PersonalGuildPvp(type, seasonId, characterId),
            GameRankingType.Pvp or GameRankingType.PvpPreviousSeason => RankDuelBoard(type, seasonId, userName),
            GameRankingType.PvpPersonal or GameRankingType.PvpPersonalPreviousSeason => PersonalRankDuel(type, seasonId, characterId),
            GameRankingType.Arcade or GameRankingType.ArcadePreviousSeason => ArcadeBoard(type, rankingId, seasonId, userName),
            GameRankingType.ArcadePersonal or GameRankingType.ArcadePersonalPreviousSeason => PersonalArcade(type, rankingId, seasonId, characterId),
            GameRankingType.Ugc or GameRankingType.UgcPreviousSeason => HomeCommend(type, seasonId, userName),
            GameRankingType.UgcPersonal or GameRankingType.UgcPersonalPreviousSeason => PersonalHomeCommend(type, seasonId, accountId),
            _ => new ByteWriter(),
        };

        return Results.File(ZlibCompress(pWriter), "application/octet-stream");
    }

    [HttpPost("ruq.aspx")]
    [RequestSizeLimit(SmallRequestLimit)]
    public async Task<IResult> Info() {
        Stream bodyStream = Request.Body;
        var memoryStream = new MemoryStream();
        await bodyStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // reset position to beginning of stream before returning
        if (memoryStream.Length == 0) {
            return Results.BadRequest("Request was empty");
        }
        IByteReader packet = new ByteReader(memoryStream.ToArray());
        int header = packet.ReadInt(); // 0?
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        int unknown = packet.ReadInt(); // 5 if mentee ?


        if (unknown != 5) {
            Log.Logger.Debug("Unimplemented info type: {Type}", unknown);
            return Results.BadRequest($"Invalid info type: {unknown}");
        }

        return Results.File(ZlibCompress(MenteeList(accountId, characterId)), "application/octet-stream");
    }

    // The client's upload (CSoapClient::UploadUgcFile, soapclient.cpp:302): i32 user type, i32 kind, i64 account,
    // i64 character, i64 resource uid, i64, i64 size, then the file. UploadPendingUgc (:129) sends only a file of 1 byte
    // to under 1 MiB, and no session token: the ids are all the request names.
    public const int UploadHeaderSize = 4 + 4 + 8 + 8 + 8 + 8 + 8;
    public const int MaxUploadFileSize = 0x100000 - 1;

    [HttpPost("urq.aspx")]
    [RequestSizeLimit(UploadHeaderSize + MaxUploadFileSize)]
    public async Task<IResult> Upload() {
        Stream bodyStream = Request.Body;
        var memoryStream = new MemoryStream();
        await bodyStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // reset position to beginning of stream before returning
        if (memoryStream.Length == 0) {
            return Results.BadRequest("Request was empty");
        }

        IByteReader packet = new ByteReader(memoryStream.ToArray());
        packet.ReadInt();
        var type = (UgcType) packet.ReadInt();
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        long ugcUid = packet.ReadLong();
        packet.ReadLong(); // the upload's own value (FUN_1418a41a0); the folder id comes from the resource
        packet.ReadLong(); // the file size

        byte[] fileBytes = packet.ReadBytes(packet.Available);

        Log.Logger.Debug("Upload: type={Type}, accountId={AccountId}, characterId={CharacterId}, ugcUid={UgcUid}", type, accountId, characterId, ugcUid);

        UgcResource? resource = null;
        if (ugcUid != 0) {
            using WebStorage.Request db = webStorage.Context();
            resource = db.GetUgc(ugcUid);
            if (resource == null) {
                return Results.NotFound($"{ugcUid} does not exist.");
            }
        }

        DateTime now = DateTime.UtcNow;
        string? refusal = UploadRefusal(type, accountId, characterId, resource, fileBytes.Length, now) ?? FileTypeRefusal(type, fileBytes);
        if (refusal == null && IsProfile(type)) {
            using GameStorage.Request db = gameStorage.Context();
            if (!db.InGame(accountId, characterId)) {
                refusal = "Character not in game.";
            } else if (type == UgcType.WeddingProfile && !db.IsMarried(characterId)) {
                refusal = "Character not married.";
            } else if (!ProfileUploads.Take(type, characterId, now)) {
                refusal = ProfileTooSoon;
            }
        } else if (refusal == null) {
            using WebStorage.Request db = webStorage.Context();
            byte part = UgcRules.UploadPart(type);
            if (!db.TakeUpload(resource!.Id, part, now)) {
                refusal = UploadTaken;
            }
        }
        // The client's retry (up to 3 tries 500 ms apart, UploadPendingUgc) of a file the server already stored, its
        // answer lost: the same bytes are answered with the stored file's key, and nothing is written.
        if (refusal is UploadTaken or ProfileTooSoon) {
            string? key = StoredKey(Paths.WEB_DATA_DIR, type, characterId, resource, fileBytes);
            if (key != null) {
                Log.Logger.Information("Upload repeated: {Key}", key);
                return Results.Text($"0,{key}");
            }
        }
        if (refusal != null) {
            Log.Logger.Warning("Upload refused: {Refusal} (type={Type}, accountId={AccountId}, characterId={CharacterId}, ugcUid={UgcUid}, size={Size})",
                refusal, type, accountId, characterId, ugcUid, fileBytes.Length);
            return Results.BadRequest(refusal);
        }

        UploadTarget? target = Target(type, characterId, resource, Guid.NewGuid().ToString());
        return target == null ? HandleUnknownMode(type) : Store(type, target, characterId, resource, now, fileBytes);
    }

    #region Ugc
    // Why an upload is refused, or null. The file is the size the client sends (1 byte to under 1 MiB). Every kind but
    // the profile picture writes to a resource the game server made for the uploading character (UgcHandler:
    // CreateUgc(type, session character)): it must exist, be that character's, and be of the kind's resource type. An
    // item icon goes with its item's resource (Item, Furniture or Mount; the client posts the image first,
    // UploadPendingUgc :114-116), a blueprint icon with its layout blueprint's. Before, any request could write to any
    // resource, and of any size.
    // A profile picture's ids must both be real (the client asks the game server nothing before it, CUgcManager
    // FUN_141882020 ugcmanager_4.cpp:1175-1205, and sends Ugc 11 with the answered path after): the controller then
    // needs that character of that account in game, and one picture per character in ProfileUploads.Gap. The wedding
    // profile picture (kind 10, CUgcManager::OpenWeddingProfileUpload ugcmanager_4.cpp:767, then Ugc 12 with the
    // path) is the same, and also needs the character married (the client uploads it only in state 2,
    // CUgcViewerWeddingProfile::vfunc_3 ugcviewerequip.cpp:799). Every other kind needs the upload the game server opened for the resource (UgcRules.UploadWindow), each file once.
    public static string? UploadRefusal(UgcType type, long accountId, long characterId, UgcResource? resource, int fileSize, DateTime now) {
        if (fileSize is < 1 or > MaxUploadFileSize) {
            return "Invalid file size.";
        }
        if (IsProfile(type)) {
            return accountId <= 0 || characterId <= 0 ? "Invalid character." : null;
        }
        if (resource == null) {
            return "Invalid UGC resource.";
        }
        if (resource.OwnerId != characterId) {
            return "Not the resource's owner.";
        }
        bool matches = type switch {
            UgcType.Item or UgcType.Furniture or UgcType.Mount or UgcType.ItemIcon => resource.Type is UgcType.Item or UgcType.Furniture or UgcType.Mount,
            UgcType.LayoutBlueprint or UgcType.BlueprintIcon => resource.Type == UgcType.LayoutBlueprint,
            _ => resource.Type == type,
        };
        if (!matches) {
            return "Wrong UGC resource type.";
        }
        if (resource.UploadUntil == null || now > resource.UploadUntil.Value) {
            return "No upload open.";
        }
        return (resource.UploadedParts & UgcRules.UploadPart(type)) != 0 ? UploadTaken : null;
    }

    // The kinds with no resource: a picture of the character, one stored file each, its turns in ProfileUploads.
    public static bool IsProfile(UgcType type) => type is UgcType.ProfileAvatar or UgcType.WeddingProfile;

    // The client re-encodes every upload before it posts it (CUgcUploadCtrl::PrepareUploadByKind,
    // ugcviewerequip.cpp:1501): a design or banner is a zlib stream of the texture (CUgcCompress::Compress,
    // ugcmanager_1.cpp:141, deflate with no header of its own) saved as .m2u; every other kind is a PNG written by
    // D3DX (the 107x107 thumbnail of a profile, wedding or guild picture, ConvertThumbnail; the blueprint picture,
    // ConvertBanner; the 48x48 icons, ConvertIcon). A file that is not of its kind's format is refused before the
    // upload is taken. Kinds with no upload are left to the target lookup.
    public static string? FileTypeRefusal(UgcType type, ReadOnlySpan<byte> file) {
        bool matches = type switch {
            UgcType.Item or UgcType.Furniture or UgcType.Mount or UgcType.Banner => IsZlib(file),
            UgcType.ProfileAvatar or UgcType.WeddingProfile or UgcType.GuildEmblem or UgcType.GuildBanner
                or UgcType.LayoutBlueprint or UgcType.ItemIcon or UgcType.BlueprintIcon => file.StartsWith(PngSignature),
            _ => true,
        };
        return matches ? null : "Invalid file type.";
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // A zlib stream header (RFC 1950): deflate (CM 8) with a window of at most 32 KiB, no preset dictionary, and the
    // check bits that make the first two bytes a multiple of 31.
    private static bool IsZlib(ReadOnlySpan<byte> file) {
        return file.Length >= 2 && (file[0] & 0x0F) == 8 && file[0] >> 4 <= 7 && (file[1] & 0x20) == 0 && (file[0] << 8 | file[1]) % 31 == 0;
    }

    public const string UploadTaken = "Upload already taken.";
    public const string ProfileTooSoon = "Profile picture too soon.";

    // The key of the file already stored under root for this upload when it holds exactly these bytes, else null. A
    // profile picture's file name is new for each upload, so its folder's one file is compared.
    public static string? StoredKey(string root, UgcType type, long characterId, UgcResource? resource, byte[] fileBytes) {
        UploadTarget? target = Target(type, characterId, resource, "");
        if (target == null) {
            return null;
        }
        string folder = Path.Combine(root, target.Folder);
        string file = Path.Combine(folder, target.File);
        if (IsProfile(type)) {
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder) : [];
            if (files.Length != 1) {
                return null;
            }
            file = files[0];
            target = Target(type, characterId, resource, Path.GetFileNameWithoutExtension(file))!;
        }
        try {
            return System.IO.File.Exists(file) && new FileInfo(file).Length == fileBytes.Length
                && System.IO.File.ReadAllBytes(file).AsSpan().SequenceEqual(fileBytes) ? target.Key : null;
        } catch (IOException) {
            return null;
        }
    }

    // Where an upload's file goes (under Paths.WEB_DATA_DIR), the key the reply answers (the client keeps it as the
    // resource's path and fetches it from the GET routes), and whether the key is stored as the resource's path. The
    // folder id is the one the game server recorded with the resource (TargetId: the design's item, the banner, the
    // guild), never the request's. Null for a kind with no upload.
    public record UploadTarget(string Folder, string File, string Key, bool SavesPath);

    public static UploadTarget? Target(UgcType type, long characterId, UgcResource? resource, string fileName) {
        if (type == UgcType.ProfileAvatar) {
            return new UploadTarget(Path.Combine("profiles", characterId.ToString()), $"{fileName}.png",
                $"data/profiles/avatar/{characterId}/{fileName}.png", false);
        }
        if (type == UgcType.WeddingProfile) {
            return new UploadTarget(Path.Combine("weddingprofiles", characterId.ToString()), $"{fileName}.png",
                $"data/profiles/wedding/{characterId}/{fileName}.png", false);
        }
        if (resource == null) {
            return null;
        }
        long id = resource.Id;
        long target = resource.TargetId;
        return type switch {
            UgcType.Item or UgcType.Mount or UgcType.Furniture => new UploadTarget(Path.Combine("items", target.ToString()), $"{id}.m2u", $"item/ms2/01/{target}/{id}.m2u", true),
            UgcType.ItemIcon => new UploadTarget(Path.Combine("itemicon", target.ToString()), $"{id}.png", $"itemicon/ms2/01/{target}/{id}.png", false),
            UgcType.Banner => new UploadTarget(Path.Combine("banner", target.ToString()), $"{id}.m2u", $"banner/ms2/01/{target}/{id}.m2u", true),
            UgcType.GuildEmblem => new UploadTarget(Path.Combine("guildmark", target.ToString()), $"{id}.png", $"guildmark/ms2/01/{target}/{id}.png", true),
            UgcType.GuildBanner => new UploadTarget(Path.Combine("guildmark", target.ToString(), "banner"), $"{id}.png", $"guildmark/ms2/01/{target}/banner/{id}.png", true),
            UgcType.BlueprintIcon => new UploadTarget(Path.Combine("blueprint", id.ToString()), $"{id}_icon.png", $"blueprint/ms2/01/{id}/{id}_icon.png", false),
            UgcType.LayoutBlueprint => new UploadTarget(Path.Combine("blueprint", id.ToString()), $"{id}.png", $"blueprint/ms2/01/{id}/{id}.png", true),
            _ => null,
        };
    }

    // Writes the file and answers its key ("0,<key>", CSoapClient::UploadUgcFile reads code and text). A profile picture
    // (or wedding profile picture) replaces the character's old one (its folder is emptied first). A file that cannot be written gives its part (or
    // the profile picture's turn) back, so the client's next try (up to 3, UploadPendingUgc) can still take it.
    private IResult Store(UgcType type, UploadTarget target, long characterId, UgcResource? resource, DateTime now, byte[] fileBytes) {
        string folder = Path.Combine(Paths.WEB_DATA_DIR, target.Folder);
        try {
            if (IsProfile(type) && Path.Exists(folder)) {
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(Path.Combine(folder, target.File), fileBytes);
        } catch (Exception ex) {
            Log.Error(ex, "Failed storing upload: {Path}", folder);
            if (resource == null) {
                ProfileUploads.GiveBack(type, characterId, now);
            } else {
                using WebStorage.Request db = webStorage.Context();
                db.GiveBackUpload(resource.Id, UgcRules.UploadPart(type));
            }
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        if (target.SavesPath) {
            using WebStorage.Request db = webStorage.Context();
            db.UpdatePath(resource!.Id, target.Key);
        }
        return Results.Text($"0,{target.Key}");
    }

    private static IResult HandleUnknownMode(UgcType mode) {
        Log.Logger.Warning("Invalid upload mode: {Mode}", mode);
        return Results.BadRequest($"Invalid upload mode: {mode}");
    }
    #endregion

    #region Ranking
    // The cache key part of a board's search: "all" for the whole board, "name:<name>" for a search (so a search
    // for the name "all" is not the whole board).
    private static string SearchKey(string? userName) => string.IsNullOrEmpty(userName) ? "all" : $"name:{userName}";

    private ByteWriter Trophy(string userName) {
        string cacheKey = $"Trophy_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            List<TrophyRankInfo> rankInfos = [];
            using GameStorage.Request db = gameStorage.Context();
            if (!string.IsNullOrEmpty(userName)) {
                TrophyRankInfo? info = db.GetTrophyRankInfo(userName);
                if (info != null) {
                    rankInfos.Add(info);
                }
            } else {
                IList<TrophyRankInfo> infos = db.GetTrophyRankings();
                rankInfos.AddRange(infos);
            }

            ByteWriter writer = InGameRankPacket.Trophy(rankInfos);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalTrophy(long characterId) {
        string cacheKey = $"PersonalTrophy_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            TrophyRankInfo? info = db.GetTrophyRankInfo(characterId);
            ByteWriter writer = InGameRankPacket.PersonalRank(GameRankingType.PersonalTrophy, info?.Rank ?? 0);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter GuildTrophy(string userName) {
        if (!string.IsNullOrEmpty(userName)) {
            string cacheKey = $"GuildTrophy_{userName}";

            if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
                // Search by guild name, check full rankings cache first then fall back to DB
                IList<GuildTrophyRankInfo> rankings = GetCachedGuildTrophyRankings();
                GuildTrophyRankInfo? cached = rankings.FirstOrDefault(r =>
                    r.Name.Equals(userName, StringComparison.OrdinalIgnoreCase));

                ByteWriter writer;
                if (cached != null) {
                    writer = InGameRankPacket.GuildTrophy([cached]);
                } else {
                    using GameStorage.Request db = gameStorage.Context();
                    GuildTrophyRankInfo? info = db.GetGuildTrophyRankInfo(userName);
                    writer = InGameRankPacket.GuildTrophy(info != null ? new[] { info } : []);
                }

                cachedData = writer.Buffer[..writer.Length];
                cache.Set(cacheKey, cachedData, RankingCacheDuration);
            }

            var result = new ByteWriter();
            result.WriteBytes(cachedData!);
            return result;
        }

        return InGameRankPacket.GuildTrophy(GetCachedGuildTrophyRankings());
    }

    private ByteWriter PersonalGuildTrophy(long characterId) {
        string cacheKey = $"PersonalGuildTrophy_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            long guildId = db.GetGuildIdByCharacterId(characterId);
            ByteWriter writer;
            if (guildId == 0) {
                writer = InGameRankPacket.PersonalRank(GameRankingType.PersonalGuildTrophy, 0);
            } else {
                // Check cached rankings for this guild's rank
                IList<GuildTrophyRankInfo> rankings = GetCachedGuildTrophyRankings();
                GuildTrophyRankInfo? cached = rankings.FirstOrDefault(r => r.GuildId == guildId);
                writer = InGameRankPacket.PersonalRank(GameRankingType.PersonalGuildTrophy, cached?.Rank ?? 0);
            }

            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter DarkStream(GameRankingType type, int jobCode, string userName) {
        string cacheKey = $"DarkStream_{type}_{jobCode}_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = DarkStreamRanking.Board(db, type, jobCode, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter RankDuelBoard(GameRankingType type, int seasonId, string userName) {
        int season = RankDuelRanking.Season(type, seasonId, tableMetadata.SeasonDataTable.Pvp, DateTime.Now);
        string cacheKey = $"RankDuel_{type}_{season}_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = RankDuelRanking.Board(db, type, season, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalRankDuel(GameRankingType type, int seasonId, long characterId) {
        int season = RankDuelRanking.Season(type, seasonId, tableMetadata.SeasonDataTable.Pvp, DateTime.Now);
        string cacheKey = $"PersonalRankDuel_{type}_{season}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = RankDuelRanking.Personal(db, type, season, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter ArcadeBoard(GameRankingType type, int arcadeId, int seasonId, string userName) {
        string cacheKey = $"Arcade_{type}_{arcadeId}_{seasonId}_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = ArcadeRanking.Board(db, type, arcadeId, seasonId, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalArcade(GameRankingType type, int arcadeId, int seasonId, long characterId) {
        string cacheKey = $"PersonalArcade_{type}_{arcadeId}_{seasonId}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = ArcadeRanking.Personal(db, type, arcadeId, seasonId, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter GuildPvpBoard(GameRankingType type, int seasonId, string userName) {
        IReadOnlyDictionary<int, Maple2.Model.Metadata.SeasonDataTable.Entry> seasons = tableMetadata.SeasonDataTable.GuildPvp;
        int season = GuildPvpRanking.Season(type, seasonId, seasons, DateTime.Now);
        string cacheKey = $"GuildPvp_{type}_{season}_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = GuildPvpRanking.Board(db, type, season, seasons, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalGuildPvp(GameRankingType type, int seasonId, long characterId) {
        int season = GuildPvpRanking.Season(type, seasonId, tableMetadata.SeasonDataTable.GuildPvp, DateTime.Now);
        string cacheKey = $"PersonalGuildPvp_{type}_{season}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = GuildPvpRanking.Personal(db, type, season, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter Colosseum(GameRankingType type, int seasonId, int jobCode, string userName) {
        int season = ColosseumRanking.Season(type, seasonId, tableMetadata.SeasonDataTable.Boss, DateTime.Now);
        string cacheKey = $"Colosseum_{type}_{season}_{jobCode}_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = ColosseumRanking.Board(db, type, season, jobCode, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalColosseum(GameRankingType type, int seasonId, int jobCode, long characterId) {
        int season = ColosseumRanking.Season(type, seasonId, tableMetadata.SeasonDataTable.Boss, DateTime.Now);
        string cacheKey = $"PersonalColosseum_{type}_{season}_{jobCode}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = ColosseumRanking.Personal(db, type, season, jobCode, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter HomeCommend(GameRankingType type, int seasonId, string userName) {
        (long Begin, long End)? month = HomeCommendRanking.Month(type, seasonId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TimeZoneInfo.Local);
        string cacheKey = $"HomeCommend_{type}_{month?.Begin}_{SearchKey(userName)}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = HomeCommendRanking.Board(db, type, month, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalHomeCommend(GameRankingType type, int seasonId, long accountId) {
        (long Begin, long End)? month = HomeCommendRanking.Month(type, seasonId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TimeZoneInfo.Local);
        string cacheKey = $"PersonalHomeCommend_{type}_{month?.Begin}_{accountId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = HomeCommendRanking.Personal(db, type, month, accountId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalDarkStream(GameRankingType type, int jobCode, long characterId) {
        string cacheKey = $"PersonalDarkStream_{type}_{jobCode}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = DarkStreamRanking.Personal(db, type, jobCode, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter Boss(GameRankingType type, int rankingId, string userName) {
        string cacheKey = $"Boss_{type}_{rankingId}_{(type == GameRankingType.BossClearCount ? SearchKey(userName) : "all")}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = BossRanking.Board(db, type, rankingId, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter WorldBoss(GameRankingType type, int rankingId, string userName) {
        string cacheKey = $"WorldBoss_{type}_{rankingId}_{(type == GameRankingType.WorldBossClearCount ? SearchKey(userName) : "all")}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = BossRanking.WorldBossBoard(db, type, rankingId, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalWorldBoss(GameRankingType type, int rankingId, long characterId) {
        string cacheKey = $"PersonalWorldBoss_{type}_{rankingId}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = BossRanking.WorldBossPersonal(db, type, rankingId, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter FameChallenge(GameRankingType type, int rankingId, string userName) {
        string cacheKey = $"FameChallenge_{type}_{rankingId}_{(type == GameRankingType.FameChallengeClearCount ? SearchKey(userName) : "all")}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = BossRanking.FameChallengeBoard(db, type, rankingId, userName ?? string.Empty);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalFameChallenge(GameRankingType type, int rankingId, long characterId) {
        string cacheKey = $"PersonalFameChallenge_{type}_{rankingId}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = BossRanking.FameChallengePersonal(db, type, rankingId, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private ByteWriter PersonalBoss(int rankingId, long characterId) {
        string cacheKey = $"PersonalBoss_{rankingId}_{characterId}";

        if (!cache.TryGetValue(cacheKey, out byte[]? cachedData)) {
            using GameStorage.Request db = gameStorage.Context();
            ByteWriter writer = BossRanking.Personal(db, rankingId, characterId);
            cachedData = writer.Buffer[..writer.Length];
            cache.Set(cacheKey, cachedData, RankingCacheDuration);
        }

        var result = new ByteWriter();
        result.WriteBytes(cachedData!);
        return result;
    }

    private IList<GuildTrophyRankInfo> GetCachedGuildTrophyRankings() {
        const string cacheKey = "GuildTrophyRankings";

        if (!cache.TryGetValue(cacheKey, out IList<GuildTrophyRankInfo>? rankings)) {
            using GameStorage.Request db = gameStorage.Context();
            rankings = db.GetGuildTrophyRankings();
            cache.Set(cacheKey, rankings, RankingCacheDuration);
        }

        return rankings!;
    }
    #endregion

    private ByteWriter MenteeList(long accountId, long characterId) {
        using GameStorage.Request db = gameStorage.Context();
        IList<long> list = db.GetMentorList(accountId, characterId);

        IList<PlayerInfo> players = [];
        foreach (long menteeId in list) {
            PlayerInfo? playerInfo = db.GetPlayerInfo(menteeId);
            if (playerInfo != null) {
                players.Add(playerInfo);
            }
        }

        return MentorPacket.MenteeList(players);
    }

    private static byte[] ZlibCompress(ByteWriter writer) {
        using var outputStream = new MemoryStream();
        using (var zlibStream = new ZLibStream(outputStream, CompressionLevel.Optimal, leaveOpen: true)) {
            zlibStream.Write(writer.Buffer, 0, writer.Length);
        }

        return outputStream.ToArray();
    }

}
