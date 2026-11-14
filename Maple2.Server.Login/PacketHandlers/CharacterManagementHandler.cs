using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Maple2.Server.Core.Helpers;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.Session;
using Maple2.Server.World.Service;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using static Maple2.Model.Enum.EquipSlot;
using static Maple2.Model.Error.CharacterCreateError;
using static Maple2.Model.Error.CharacterDeleteError;
using static Maple2.Model.Error.MigrationError;
using Enum = System.Enum;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Login.PacketHandlers;

public class CharacterManagementHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.CharacterManagement;

    private enum Command : byte {
        Select = 0,
        Create = 1,
        Delete = 2,
        CancelDelete = 3,
        ConfirmDelete = 4,
        Rename = 5,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required GameStorage GameStorage { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(LoginSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        if (session.LeftCharacterListRefuses($"CharacterManagement {command}")) {
            return;
        }
        if (command == Command.Select) {
            HandleSelect(session, packet);
            return;
        }
        if (command is not (Command.Create or Command.Delete or Command.CancelDelete or Command.ConfirmDelete or Command.Rename)) {
            throw new ArgumentException($"Invalid CHARACTER_MANAGEMENT type {command}");
        }

        long characterId = command == Command.Create ? 0 : packet.ReadLong();
        string name = command == Command.Rename ? packet.ReadUnicodeString() : string.Empty;
        try {
            switch (command) {
                case Command.Create:
                    HandleCreate(session, packet);
                    break;
                case Command.Delete:
                    HandleDelete(session, characterId);
                    break;
                case Command.CancelDelete:
                    HandleCancelDelete(session, characterId);
                    break;
                case Command.ConfirmDelete:
                    HandleDelete(session, characterId, final: true);
                    break;
                case Command.Rename:
                    HandleRename(session, characterId, name);
                    break;
            }
        } catch (Exception ex) when (ex is not PacketOverreadException) {
            // A failure the checks do not answer (the database, say) gets the request's own system error. The client
            // takes no list input until its answer comes (UICharacterListDialog), and the session's read loop would
            // otherwise end the connection on the exception, with no box to say why. A packet the client could not
            // have written is still dropped by the router.
            Logger.Error(ex, "CharacterManagement {Command} failed for character {CharacterId}", command, characterId);
            session.Send(command switch {
                Command.Create => CharacterListPacket.CreateError(s_char_err_system),
                Command.CancelDelete => CharacterListPacket.CancelDelete(characterId, s_char_err_destroy),
                Command.Rename => CharacterListPacket.NameRefused(NameChangeRules.Refusal.Invalid, characterId, name),
                _ => DeleteRefused(characterId, StoredDeleteTime(session, characterId), s_char_err_destroy),
            });
        }
    }

    // The delete time a refused delete is answered with (DeleteRefused); 0 when it cannot be read either.
    private long StoredDeleteTime(LoginSession session, long characterId) {
        try {
            using GameStorage.Request db = GameStorage.Context();
            return db.GetCharacter(characterId, session.AccountId)?.DeleteTime ?? 0;
        } catch (Exception ex) {
            Logger.Error(ex, "Delete time of character {CharacterId} not read", characterId);
            return 0;
        }
    }

    private void HandleSelect(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        // The channel picked on the server list (UIServerListDialog::EnterGame), 0 when none was picked.
        short channel = packet.ReadShort();

        try {
            using GameStorage.Request db = GameStorage.Context();
            Character? character = db.GetCharacter(characterId, session.AccountId);
            if (character == null) {
                session.Send(MigrationPacket.LoginToGameError(s_move_err_default, "Invalid character"));
                return;
            }
            // The client never starts a character in delete wait (UICharacterListDialog OnGameStart returns when
            // its delete time is set).
            if (character.DeleteTime != default) {
                session.Send(MigrationPacket.LoginToGameError(s_move_err_default, "Character is waiting for deletion"));
                return;
            }
            // A name that is too long: the client opens its name input and asks for the rename.
            if (LoginSession.IsRenameDue(character.Name)) {
                session.Send(MigrationPacket.LoginToGameRename(characterId));
                return;
            }

            EndGameSession(session);

            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = characterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                EndsEarlier = true,
            };
            if (channel > 0) {
                request.Channel = channel;
                request.ExactChannel = true;
            }

            Logger.Information("Logging in to game as {Request}", request);

            MigrateOutResponse response = World.MigrateOut(request);
            if (!EndpointHelper.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Login to game", response.IpAddress, response.Port);
                session.Send(MigrationPacket.LoginToGameError(s_move_err_default, "Invalid game server address"));
                session.CutAfterSend();
                return;
            }
            session.Send(MigrationPacket.LoginToGame(endpoint, response.Token, character.MapId));
            session.Disconnect();
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_no_server, ex.Message));
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_member_limit, ex.Message));
        } catch (RpcException ex) {
            session.Send(MigrationPacket.LoginToGameError(s_move_err_default, ex.Message));
            session.CutAfterSend();
        } catch (Exception ex) {
            // A failed load (a bad stored row, say) would otherwise leave only the client's error code.
            Logger.Error(ex, "Login to game failed for character {CharacterId}", characterId);
            session.Send(MigrationPacket.LoginToGameError(s_move_err_default, ex.Message));
            session.CutAfterSend();
        }
    }

    // The account still in game: another client of it entered with a character it picked after this login was let
    // in (a login is refused only while the account is in game, LoginHandler), so both would play. That session is
    // ended first, as a login of an account in game ends it. Its end saves it before the answer comes back (the
    // channel's Disconnect cuts the session, and the cut runs its Dispose and its save), so the entry of this
    // character loads what it saved. A record that went offline in between is not found: nothing to end.
    private void EndGameSession(LoginSession session) {
        PlayerInfoResponse online = World.AccountInfo(new PlayerInfoRequest { AccountId = session.AccountId });
        if (online is not { CharacterId: > 0 } || online.Channel == -1) {
            return;
        }

        Logger.Information("Account {AccountId} picked a character while {CharacterId} is in game on channel {Channel}: ending that session",
            session.AccountId, online.CharacterId, online.Channel);
        try {
            World.Disconnect(new DisconnectRequest { CharacterId = online.CharacterId, Force = true });
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) {
            Logger.Information("Character {CharacterId} went offline before the disconnect", online.CharacterId);
        }
    }

    // The forced rename of the login scene: the client sends it from the name input it opens for a character whose
    // name is too long (the character list's end flag, or LoginToGame 49). It sends the character and the name
    // only, no item, so nothing is used up. The list takes no input from the moment the name input opens until
    // CharacterList 7 arrives (UICharacterListDialog::vfunc_79) or the list is sent again: a name that is accepted
    // shows in the character list, which is sent again; a refused name is answered with CharacterList 7 and its
    // code, for which the client shows its own name change text, and the character stays due.
    private void HandleRename(LoginSession session, long characterId, string name) {
        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null || !LoginSession.IsRenameDue(character.Name)) {
            session.Send(CharacterListPacket.NameRefused(NameChangeRules.Refusal.Invalid, characterId, name));
            return;
        }

        // The same checks as a create.
        int width = NameValidator.CharacterNameWidth(name, Target.LOCALE);
        bool blank = CharacterCreateRules.IsBlankName(name);
        bool banned = !blank && BanWordStorage.ContainsBannedWord(name);
        bool taken = !blank && db.GetCharacterId(name) != default;
        NameChangeRules.Refusal refusal = NameChangeRules.Check(blank, width, Constant.CharacterNameLengthMin, Constant.CharacterNameLengthMax, banned, taken);
        // The write refuses a name that was taken since the check; a write that failed for another reason gets the
        // system text.
        if (refusal == NameChangeRules.Refusal.None) {
            refusal = db.RenameCharacter(session.AccountId, characterId, name);
        }
        if (refusal != NameChangeRules.Refusal.None) {
            session.Send(CharacterListPacket.NameRefused(refusal, characterId, name));
            return;
        }

        // The world tells the guild and club members that are online (Guild 12, Club 27).
        try {
            World.UpdatePlayer(new PlayerUpdateRequest {
                AccountId = session.AccountId,
                CharacterId = characterId,
                Name = name,
            });
        } catch (RpcException ex) {
            Logger.Warning("Rename of character {CharacterId}: the world was not told ({Status})", characterId, ex.StatusCode);
        }

        // The rename is stored: a failure from here on is logged, not answered with a refusal of the stored name.
        try {
            session.ListCharacters();
        } catch (Exception ex) {
            Logger.Error(ex, "Rename of character {CharacterId}: the list was not sent", characterId);
        }
    }

    private void HandleCreate(LoginSession session, IByteReader packet) {
        var gender = packet.Read<Gender>();
        var jobCode = packet.Read<JobCode>();
        var job = (Job) ((int) jobCode * 10);
        string name = packet.ReadUnicodeString();

        // A create before the character list was sent has no account to count slots for.
        if (!session.HasAccount) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        // The client sends gender 0 or 1; the job must be one of the eleven the client's create reader allows
        // (job.xml also holds the beginner row) and one the job table holds.
        if (!CharacterCreateRules.IsCreateGender(gender)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }
        if (!CharacterCreateRules.IsCreateJob(jobCode) || !TableMetadata.JobTable.Entries.TryGetValue(jobCode, out JobTable.Entry? entry)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_job_forbidden));
            return;
        }

        if (string.IsNullOrWhiteSpace(name)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }

        // Counted the way the client counts for its region (Hangul and CJK count twice).
        int width = NameValidator.CharacterNameWidth(name, Target.LOCALE);
        if (width < 0) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_all));
            return;
        }
        if (width < Constant.CharacterNameLengthMin) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }
        if (width > Constant.CharacterNameLengthMax) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        // The client fills its text ("Contains a forbidden word ({0}).") with the answer's text: the word found.
        if (BanWordStorage.FindBannedWord(name) is { } banned) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_any, banned));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        if (!CharacterCreateRules.HasFreeSlot(db.CountCharacters(session.AccountId), session.MaxCharacters, Constant.ServerMaxCharacters)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_char_count));
            return;
        }
        long existingId = db.GetCharacterId(name);
        if (existingId != 0) {
            session.Send(CharacterListPacket.CreateError(s_char_err_already_taken));
            return;
        }

        var skinColor = packet.Read<SkinColor>();
        packet.Skip(2); // Unknown

        var outfits = new List<Item>();
        int equipCount = packet.ReadByte();
        for (int i = 0; i < equipCount; i++) {
            int id = packet.ReadInt();
            byte slotByte = packet.ReadByte();
            EquipSlot slot = (EquipSlot) slotByte;
            if (slot is SK or OH or Unknown) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }
            if (!ValidateDefaultItems(jobCode, id, slot)) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }
            if (!ItemMetadata.TryGet(id, out ItemMetadata? metadata)) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }
            if (metadata.Limit.Gender != Gender.All && metadata.Limit.Gender != gender) {
                session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
                return;
            }

            var outfit = new Item(metadata) {
                Group = ItemGroup.Outfit,
                Slot = (short) slot,
            };
            Debug.Assert(outfit.Appearance != null, "equip.Appearance == null");
            outfit.Appearance.ReadFrom(packet);
            outfits.Add(outfit);
        }

        packet.Skip(4); // Unknown

        // Each item is a default item of its slot; no slot may come twice.
        if (!CharacterCreateRules.IsCreateOutfit(outfits.Select(outfit => (EquipSlot) outfit.Slot).ToList())) {
            session.Send(CharacterListPacket.CreateError(s_char_err_invalid_def_item));
            return;
        }

        var character = new Character {
            AccountId = session.AccountId,
            Gender = gender,
            Job = job,
            Name = name,
            SkinColor = skinColor,
            MapId = entry.Tutorial.StartField,
            ReturnMaps = new LimitedStack<int>(3, entry.Tutorial.StartField),
            Mastery = new Mastery(),
        };

        session.CreateCharacter(character, outfits);
    }

    // final: the final delete (CharacterManagement 4), which the client sends only from the final delete button of
    // a character in wait (UICharacterListDialog onClickFinalDelete). Without a wait it is refused with error 8 in
    // the delete answer, which lets the list take input again; it used to start a wait or delete outright.
    private void HandleDelete(LoginSession session, long characterId, bool final = false) {
        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null || !ValidateDeleteRequest(session, characterId, db, character)) {
            return;
        }
        if (final && character.DeleteTime == default) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_no_destroy_wait));
            return;
        }

        // Delete already pending
        if (character.DeleteTime != default) {
            if (character.DeleteTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                DeleteCharacter(session, db, characterId, character.DeleteTime);
            } else {
                session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime,
                    s_char_err_next_delete_char_date));
            }
            return;
        }

        if (character.Level >= Constants.CharacterDestroyDivisionLevel) {
            character.DeleteTime = DateTimeOffset.UtcNow.AddSeconds(Constants.CharacterDestroyWaitSecond).ToUnixTimeSeconds();
            if (db.UpdateDelete(session.AccountId, characterId, character.DeleteTime)) {
                session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime));
            } else {
                session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime, s_char_err_destroy));
            }
        } else {
            DeleteCharacter(session, db, characterId, character.DeleteTime);
        }
    }

    // The restore of a character whose delete is pending. It only ends the wait: the blocks of a delete (a guild,
    // unread mail, listings, a marriage) are for the delete itself. Every answer is CharacterList 6, whose error the
    // client shows with its delete texts (UICharacterListDialog mode 6). The client zeroes the character's countdown
    // when it sends the restore (CancelDelete), so a refused restore of a character still in its wait also sends the
    // delete time again (mode 5 with no error, no box), or the client would offer the final delete at once.
    private void HandleCancelDelete(LoginSession session, long characterId) {
        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null) {
            session.Send(CharacterListPacket.CancelDelete(characterId, s_char_err_already_destroy));
            return;
        }

        if (character.DeleteTime == default) {
            session.Send(CharacterListPacket.CancelDelete(characterId, s_char_err_no_destroy_wait));
            return;
        }

        if (!db.UpdateDelete(session.AccountId, characterId, 0)) {
            session.Send(CharacterListPacket.CancelDelete(characterId, s_char_err_destroy));
            session.Send(CharacterListPacket.BeginDelete(characterId, character.DeleteTime));
            return;
        }
        session.Send(CharacterListPacket.CancelDelete(characterId));
    }

    private bool ValidateDefaultItems(JobCode jobCode, int id, EquipSlot slot) {
        if (TableMetadata.DefaultItemsTable.Common.TryGetValue(slot, out int[]? commonItemIds) && commonItemIds.Contains(id)) {
            return true;
        }
        if (TableMetadata.DefaultItemsTable.Job.TryGetValue(jobCode, slot, out int[]? jobItemIds) && jobItemIds.Contains(id)) {
            return true;
        }

        return false;
    }

    private static bool ValidateDeleteRequest(LoginSession session, long characterId, GameStorage.Request db, Character? character) {
        // This character does not exist or is not owned by session's account.
        if (character == null) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_already_destroy));
            return false;
        }

        if (db.GetBlackMarketListings(characterId).Any()) {
            session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_black_market_count));
            return false;
        }

        if (db.GetMyMesoListingsByCharacterId(characterId).Any()) {
            session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_meso_market_count));
            return false;
        }

        if (db.GetAllMail(characterId).Any(mail => mail.ReadTime == 0)) {
            session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_unread_mail));
            return false;
        }

        // The guild is not a character column: the member row holds it.
        long guildId = db.GetGuildId(characterId);
        if (guildId != 0) {
            if (db.GetGuild(guildId)?.LeaderCharacterId == characterId) {
                session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_guild_master));
                return false;
            }

            session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_guild));
            return false;
        }

        if (db.GetUgcListingsByCharacterId(characterId).Any()) {
            session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_ugc_market));
            return false;
        }

        if (character.MarriageInfo.Status != MaritalStatus.Single) {
            session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_wedding));
            return false;
        }

        // s_char_err_exist_ugc_map: an account that owns an outdoor plot keeps at least one active character, one
        // with no delete pending, besides this one. A character whose delete is already pending is not active, so
        // completing its delete takes no active character away and is not refused.
        if (character.DeleteTime == default && db.OwnsOutdoorPlot(session.AccountId)) {
            (_, IList<Character>? characters) = db.ListCharacters(session.AccountId);
            if (characters == null || !characters.Any(other => other.Id != characterId && other.DeleteTime == default)) {
                session.Send(DeleteRefused(characterId, character.DeleteTime, s_char_err_exist_ugc_map));
                return false;
            }
        }

        return true;
    }

    // A refused delete. The answer to a character in its wait is the wait's answer (CharacterList 5) with the error:
    // the client shows the error and keeps the character's panel ready for the final delete or its cancel
    // (UICharacterListDialog mode 5). The delete answer (mode 2) with an error zeroes the client's timer and closes
    // that panel but keeps the character's delete time, and the client starts no character with one (OnGameStart):
    // the character could then be neither started nor restored until the list was loaded again.
    private static ByteWriter DeleteRefused(long characterId, long deleteTime, CharacterDeleteError error) {
        return deleteTime != default
            ? CharacterListPacket.BeginDelete(characterId, deleteTime, error)
            : CharacterListPacket.DeleteEntry(characterId, error);
    }

    private void DeleteCharacter(LoginSession session, GameStorage.Request db, long characterId, long deleteTime) {
        // The world takes the character out of its clubs first (the other members get Club 0x1c, a leader hands the
        // club on): the club rows name the character, so deleting it first would take a led club with it.
        try {
            Maple2.Server.World.Service.ClubResponse response = World.Club(new ClubRequest {
                RequestorId = characterId,
                DeleteCharacter = new ClubRequest.Types.DeleteCharacter(),
            });
            if (response.Error != 0) {
                Logger.Warning("Delete of character {CharacterId}: the world could not take it out of its clubs ({Error})", characterId, response.Error);
                session.Send(DeleteRefused(characterId, deleteTime, s_char_err_destroy));
                return;
            }
        } catch (RpcException ex) {
            Logger.Warning("Delete of character {CharacterId}: the world was not reached ({Status})", characterId, ex.StatusCode);
            session.Send(DeleteRefused(characterId, deleteTime, s_char_err_destroy));
            return;
        }

        if (!db.DeleteCharacter(session.AccountId, characterId)) {
            session.Send(DeleteRefused(characterId, deleteTime, s_char_err_destroy));
            return;
        }

        session.Send(CharacterListPacket.DeleteEntry(characterId));
        // The delete is answered: a failure from here on is logged, not answered again (a second DeleteEntry with an
        // error would show the delete error box for a character that is gone).
        try {
            session.ReleaseBurningLevelup(db, characterId);
        } catch (Exception ex) {
            Logger.Error(ex, "Delete of character {CharacterId}: its burning levelup pick was not freed", characterId);
        }
    }
}
