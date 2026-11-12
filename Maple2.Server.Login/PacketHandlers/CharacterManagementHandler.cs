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
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
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
        switch (command) {
            case Command.Select:
                HandleSelect(session, packet);
                break;
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.Delete:
                HandleDelete(session, packet);
                break;
            case Command.CancelDelete:
                HandleCancelDelete(session, packet);
                break;
            case Command.ConfirmDelete:
                HandleDelete(session, packet);
                break;
            case Command.Rename:
                HandleRename(session, packet);
                break;
            default:
                throw new ArgumentException($"Invalid CHARACTER_MANAGEMENT type {command}");
        }
    }

    private void HandleSelect(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        packet.ReadShort(); // 01 00, world? channel?

        try {
            using GameStorage.Request db = GameStorage.Context();
            Character? character = db.GetCharacter(characterId, session.AccountId);
            if (character == null) {
                session.Send(MigrationPacket.LoginToGameError(s_move_err_default, "Invalid character"));
                return;
            }
            // A name that is too long: the client opens its name input and asks for the rename.
            if (LoginSession.IsRenameDue(character.Name)) {
                session.Send(MigrationPacket.LoginToGameRename(characterId));
                return;
            }

            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = characterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
            };

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

    // The forced rename of the login scene: the client sends it from the name input it opens for a character whose
    // name is too long (the character list's end flag, or LoginToGame 49). It sends the character and the name
    // only, no item, so nothing is used up. The list takes no input from the moment the name input opens until
    // CharacterList 7 arrives (UICharacterListDialog::vfunc_79) or the list is sent again: a name that is accepted
    // shows in the character list, which is sent again; a refused name is answered with CharacterList 7 and its
    // code, for which the client shows its own name change text, and the character stays due.
    private void HandleRename(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        string name = packet.ReadUnicodeString();

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

        session.ListCharacters();
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

        // The client sends gender 0 or 1; the job must be one the job table (job.xml) holds.
        if (!CharacterCreateRules.IsCreateGender(gender)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }
        if (!TableMetadata.JobTable.Entries.TryGetValue(jobCode, out JobTable.Entry? entry)) {
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

        if (BanWordStorage.ContainsBannedWord(name)) {
            session.Send(CharacterListPacket.CreateError(s_char_err_ban_any));
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

    private void HandleDelete(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();

        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null || !ValidateDeleteRequest(session, characterId, db, character)) {
            return;
        }

        // Delete already pending
        if (character.DeleteTime != default) {
            if (character.DeleteTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                DeleteCharacter(session, db, characterId);
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
            DeleteCharacter(session, db, characterId);
        }
    }

    private void HandleCancelDelete(LoginSession session, IByteReader packet) {
        long characterId = packet.ReadLong();

        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null || !ValidateDeleteRequest(session, characterId, db, character)) {
            return;
        }

        if (character.DeleteTime == default) {
            session.Send(CharacterListPacket.CancelDelete(characterId, s_char_err_no_destroy_wait));
            return;
        }

        // Remove DeleteTime
        character.DeleteTime = 0;
        if (db.UpdateDelete(session.AccountId, characterId, 0)) {
            session.Send(CharacterListPacket.CancelDelete(characterId));
        }
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
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_black_market_count));
            return false;
        }

        if (db.GetMyMesoListingsByCharacterId(characterId).Any()) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_meso_market_count));
            return false;
        }

        if (db.GetAllMail(characterId).Any(mail => mail.ReadTime == 0)) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_unread_mail));
            return false;
        }

        if (character.GuildId != 0) {
            if (db.GetGuild(character.GuildId)?.LeaderCharacterId == characterId) {
                session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_guild_master));
                return false;
            }

            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_guild));
            return false;
        }

        if (db.GetUgcListingsByCharacterId(characterId).Any()) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_ugc_market));
            return false;
        }

        if (character.MarriageInfo.Status != MaritalStatus.Single) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_wedding));
            return false;
        }

        return true;
    }

    private static void DeleteCharacter(LoginSession session, GameStorage.Request db, long characterId) {
        if (!db.DeleteCharacter(session.AccountId, characterId)) {
            session.Send(CharacterListPacket.DeleteEntry(characterId, s_char_err_destroy));
            return;
        }

        session.Send(CharacterListPacket.DeleteEntry(characterId));
    }
}
