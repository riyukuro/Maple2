using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets.Helper;
using Maple2.Tools.Extensions;
using Equips = System.Collections.Generic.IDictionary<Maple2.Model.Enum.ItemGroup,
    System.Collections.Generic.List<Maple2.Model.Game.Item>>;

namespace Maple2.Server.Core.Packets;

public static class CharacterListPacket {
    private enum Command : byte {
        List = 0,
        AppendEntry = 1,
        DeleteEntry = 2,
        StartList = 3,
        EndList = 4,
        BeginDelete = 5,
        CancelDelete = 6,
        NameChanged = 7,
    }

    public static ByteWriter AddEntries(Account account, ICollection<(Character, Equips)> entry) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.List);
        pWriter.WriteByte((byte) entry.Count); // CharCount
        foreach ((Character character, Equips equips) in entry) {
            pWriter.WriteEntry(account, character, equips);
        }

        return pWriter;
    }

    // Sent after creating a character to append to list
    public static ByteWriter AppendEntry(Account account, Character character, Equips equips) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.AppendEntry);
        pWriter.WriteEntry(account, character, equips);
        return pWriter;
    }

    public static ByteWriter DeleteEntry(long characterId, CharacterDeleteError error = default) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.DeleteEntry);
        pWriter.Write<CharacterDeleteError>(error);
        pWriter.WriteLong(characterId);
        return pWriter;
    }

    public static ByteWriter BeginDelete(long characterId, long deleteTime, CharacterDeleteError error = default) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.BeginDelete);
        pWriter.WriteLong(characterId);
        pWriter.Write<CharacterDeleteError>(error);
        pWriter.WriteLong(deleteTime);
        return pWriter;
    }

    public static ByteWriter CancelDelete(long characterId, CharacterDeleteError error = default) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.CancelDelete);
        pWriter.WriteLong(characterId);
        pWriter.Write<CharacterDeleteError>(error);
        return pWriter;
    }

    // The answer to the name change item, as KMS2 sends it: the result, the character and its new name. The client
    // reads only the result; 1 makes it quit to the character list (CSceneStateGame::OnCharacterList).
    public static ByteWriter NameChanged(long characterId, string name) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.NameChanged);
        pWriter.WriteInt(1); // accepted
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);
        return pWriter;
    }

    // The answer to a refused rename on the character list (UICharacterListDialog::vfunc_79, opcode 0xc type 7):
    // the list takes input again, and the client shows its own text with the name: 2 the taken text, 6 the bad
    // word text, any other code but 1 the system text with the code.
    public static ByteWriter NameRefused(NameChangeRules.Refusal refusal, long characterId, string name) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.NameChanged);
        pWriter.WriteInt(refusal switch {
            NameChangeRules.Refusal.Taken => 2,
            NameChangeRules.Refusal.Banned => 6,
            _ => 0,
        });
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);
        return pWriter;
    }

    public static ByteWriter StartList() {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.StartList);
        return pWriter;
    }

    // This only needs to be sent if char count > 0
    // checkNames: the client then tests the name of the character it is about to start with and, for a name that is
    // too long, opens its name input instead of asking to enter (UICharacterListDialog).
    public static ByteWriter EndList(bool checkNames = false) {
        var pWriter = Packet.Of(SendOp.CharacterList);
        pWriter.Write<Command>(Command.EndList);
        pWriter.WriteBool(checkNames);
        return pWriter;
    }

    public static ByteWriter CreateError(CharacterCreateError error, string message = "") {
        var pWriter = Packet.Of(SendOp.CharacterCreate);
        pWriter.Write<CharacterCreateError>(error);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter SetMax(int unlocked, int total) {
        var pWriter = Packet.Of(SendOp.CharMaxCount);
        pWriter.WriteInt(unlocked);
        pWriter.WriteInt(total);
        return pWriter;
    }

    private static void WriteEntry(this IByteWriter pWriter, Account account, Character character, Equips equips) {
        pWriter.WriteCharacter(account, character);
        pWriter.WriteUnicodeString(character.Picture);
        pWriter.WriteLong(character.DeleteTime);

        equips.TryGetValue(ItemGroup.Gear, out List<Item>? gears);
        gears ??= [];
        equips.TryGetValue(ItemGroup.Outfit, out List<Item>? outfits);
        outfits ??= [];
        pWriter.WriteByte((byte) (gears.Count + outfits.Count));
        foreach (Item gear in gears) {
            pWriter.WriteEquip(gear);
        }
        foreach (Item outfit in outfits) {
            pWriter.WriteEquip(outfit);
        }

        equips.TryGetValue(ItemGroup.Badge, out List<Item>? badges);
        badges ??= [];
        pWriter.WriteByte((byte) badges.Count);
        foreach (Item badge in badges) {
            pWriter.WriteBadge(badge);
        }

        // The second outfit tab (CCharacterInfo::Read): bool, then the tab's period and its items.
        equips.TryGetValue(ItemGroup.Outfit2, out List<Item>? outfits2);
        outfits2 ??= [];
        bool hasSkinTab = character.SkinTabStart != 0 || character.SkinTabEnd != 0 || outfits2.Count > 0;
        pWriter.WriteBool(hasSkinTab);
        if (hasSkinTab) {
            pWriter.WriteLong(character.SkinTabStart);
            pWriter.WriteLong(character.SkinTabEnd);
            pWriter.WriteByte((byte) outfits2.Count);
            foreach (Item outfit in outfits2) {
                pWriter.WriteEquip(outfit);
            }
        }
    }

    private static void WriteCharacter(this IByteWriter writer, Account account, Character character) {
        writer.WriteLong(account.Id);
        writer.WriteLong(character.Id);
        writer.WriteUnicodeString(character.Name);
        writer.Write<Gender>(character.Gender);
        writer.WriteByte(AdminLevel.Of(account.AdminPermissions)); // GM grade: 50 or more shows as a GM
        writer.WriteLong();
        writer.WriteInt(); // read and discarded by client
        writer.WriteInt(character.MapId);
        writer.WriteInt(character.InstanceMapId);
        writer.WriteInt(character.RoomId);
        writer.WriteShort(character.Level);
        writer.WriteShort(character.Channel);
        writer.WriteInt((int) character.Job.Code());
        writer.Write<Job>(character.Job);
        writer.WriteInt(); // CurrentHp
        writer.WriteInt(); // MaxHp
        writer.WriteShort();
        writer.WriteLong(); // read and discarded by client
        writer.WriteLong(character.StorageCooldown); // charObj+0x1184, set by HomeBank (0xDC)
        writer.WriteLong(character.DoctorCooldown); // charObj+0x1188, set by HomeDoctor (0xDD)
        writer.WriteInt(character.ReturnMaps.Peek());
        writer.Write<Vector3>(character.ReturnPosition);
        writer.WriteInt(); // GearScore
        writer.Write<SkinColor>(character.SkinColor);
        writer.WriteLong(character.CreationTime);
        writer.Write<AchievementInfo>(character.AchievementInfo);
        writer.WriteLong(character.GuildId);
        writer.WriteUnicodeString(character.GuildName);
        writer.WriteUnicodeString(character.Motto);
        writer.WriteUnicodeString(character.Picture);
        writer.WriteByte(); // Club Count
        writer.WriteByte(); // charObj+0xff8, set by SetPcBang (0xB5)
        writer.WriteClass<Mastery>(character.Mastery);
        PlatformBlock.Write(writer); // the region's platform part (PlatformBlock)
        writer.WriteInt(); // count for long list
        writer.WriteByte(); // MentorRole (charObj+0x11cc)
        writer.WriteBool(false); // read and discarded by client
        writer.WriteLong(); // Birthday (charObj+0x11e8, set by BirthdayCard 0x111)
        writer.WriteInt(); // SuperChatId (charObj+0x11f0, set/cleared by SuperWorldChat 0x112)
        // charObj+0x11f4 and +0x11f8: the daily pet compose count and its time. The character list reads them
        // into its own copy of the user block (UICharacterListDialog::CCharacterInfo::Read) and never uses
        // them; only the field's add-user block sets them on the player (CSceneStateGame::OnAddUser), so 0 here.
        writer.WriteInt(); // charObj+0x11f4, daily pet compose count
        writer.WriteLong(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // charObj+0x11f8, its time
        writer.WriteInt(account.PrestigeLevel);
        writer.WriteLong(character.LastModified.ToEpochSeconds());
        writer.WriteInt(); // count for long list (charObj+0x1B8 region)
        writer.WriteInt(); // count for long list (charObj+0x1E8 region)
        writer.WriteShort(); // charObj+0x120c, Survival (triggers field player refresh)
        writer.WriteLong();
    }
}
