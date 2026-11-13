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

    [HttpPost("irrq.aspx")]
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
            GameRankingType.Ugc or GameRankingType.UgcPreviousSeason => HomeCommend(type, seasonId, userName),
            GameRankingType.UgcPersonal or GameRankingType.UgcPersonalPreviousSeason => PersonalHomeCommend(type, seasonId, accountId),
            _ => new ByteWriter(),
        };

        return Results.File(ZlibCompress(pWriter), "application/octet-stream");
    }

    [HttpPost("ruq.aspx")]
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

    [HttpPost("urq.aspx")]
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
        int id = packet.ReadInt(); // item id, guild id, others?
        packet.ReadInt();
        packet.ReadLong();

        byte[] fileBytes = packet.ReadBytes(packet.Available);

        Log.Logger.Debug("Upload: type={Type}, accountId={AccountId}, characterId={CharacterId}, ugcUid={UgcUid}, id={Id}", type, accountId, characterId, ugcUid, id);

        UgcResource? resource = null;
        if (ugcUid != 0) {
            using WebStorage.Request db = webStorage.Context();
            resource = db.GetUgc(ugcUid);
            if (resource == null) {
                return Results.NotFound($"{ugcUid} does not exist.");
            }
        }

        if (type is UgcType.ItemIcon or UgcType.Item or UgcType.BlueprintIcon or UgcType.LayoutBlueprint && resource == null) {
            return Results.BadRequest("Invalid UGC resource.");
        }

        return type switch {
            UgcType.ProfileAvatar => UploadProfileAvatar(fileBytes, characterId),
            UgcType.Item or UgcType.Mount or UgcType.Furniture => UploadItem(fileBytes, id, ugcUid, resource!),
            UgcType.ItemIcon => UploadItemIcon(fileBytes, id, ugcUid, resource!),
            UgcType.Banner => UploadBanner(fileBytes, id, ugcUid),
            UgcType.GuildEmblem => HandleGuildEmblem(fileBytes, id, ugcUid),
            UgcType.GuildBanner => HandleGuildBanner(fileBytes, id, ugcUid),
            UgcType.BlueprintIcon => HandleBlueprintIcon(fileBytes, ugcUid, resource!),
            UgcType.LayoutBlueprint => HandleBlueprintPreview(fileBytes, ugcUid, resource!),
            _ => HandleUnknownMode(type),
        };
    }

    #region Ugc
    private static IResult UploadProfileAvatar(byte[] fileBytes, long characterId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "profiles", characterId.ToString());
        try {
            // Deleting old files in the character folder
            if (Path.Exists(filePath)) {
                Directory.Delete(filePath, true);
            }
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }

        string uniqueFileName = Guid.NewGuid().ToString();
        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{uniqueFileName}.png"), fileBytes);
        return Results.Text($"0,data/profiles/avatar/{characterId}/{uniqueFileName}.png");
    }

    private IResult UploadItem(byte[] fileBytes, int itemId, long ugcId, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "items", itemId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"item/ms2/01/{itemId}/{resource.Id}.m2u";

        db.UpdatePath(ugcId, ugcPath);
        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{resource.Id}.m2u"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult UploadItemIcon(byte[] fileBytes, int itemId, long ugcId, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "itemicon", itemId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        //TODO: Verify that the item exists in the database
        string ugcPath = $"itemicon/ms2/01/{itemId}/{resource.Id}.png";

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult UploadBanner(byte[] fileBytes, int bannerId, long ugcId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "banner", bannerId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"banner/ms2/01/{bannerId}/{ugcId}.m2u";

        db.UpdatePath(ugcId, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.m2u"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleGuildEmblem(byte[] fileBytes, int guildId, long ugcId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "guildmark", guildId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"guildmark/ms2/01/{guildId}/{ugcId}.png";

        db.UpdatePath(ugcId, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleGuildBanner(byte[] fileBytes, int guildId, long ugcId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "guildmark", guildId.ToString(), "banner");
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"guildmark/ms2/01/{guildId}/banner/{ugcId}.png";

        db.UpdatePath(ugcId, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleBlueprintIcon(byte[] fileBytes, long ugcUid, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "blueprint", ugcUid.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }

        string ugcPath = $"blueprint/ms2/01/{ugcUid}/{resource.Id}_icon.png";

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{resource.Id}_icon.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleBlueprintPreview(byte[] fileBytes, long ugcUid, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "blueprint", ugcUid.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"blueprint/ms2/01/{ugcUid}/{resource.Id}.png";

        db.UpdatePath(ugcUid, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{resource.Id}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
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
