using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;
using Maple2.Model.Game.Party;
// ReSharper disable RedundantTypeArgumentsOfMethod

namespace Maple2.Server.Game.Packets;

public static class PartyPacket {
    private enum Command : byte {
        Error = 0,
        Joined = 2,
        Leave = 3,
        Kicked = 4,
        NotifyLogin = 5,
        NotifyLogout = 6,
        Disbanded = 7,
        NotifyUpdateLeader = 8,
        Load = 9,
        Invited = 11,
        UpdateMember = 12,
        UpdateMember2 = 13,
        //13 - duplicate of 12
        UpdateDungeonInfo = 14,
        Unknown1 = 15,
        Tombstone = 18,
        UpdateStats = 19,
        DungeonNotice = 20,
        NoticeWithSound = 21, // OnPartyNoticeWithSound (partymanager.cpp:1281)
        DungeonReset = 25,
        PartySearchListing = 26,
        PartySearch = 30,
        PartySearchDungeon = 31,
        DungeonRecord = 35,
        MemberNotice = 37, // FUN_14162ee70 (partymanager.cpp:1357)
        DungeonHelperCooldown = 40,
        JoinRequest = 44,
        StartVote = 47,
        ReadyCheck = 48,
        EndVote = 49,
        SurvivalPartySearch = 55, // OnPartySurvivalSquadRegister (partymanager.cpp:473)
        DungeonRoomCreate = 56, // OnPartyDungeonRoomCreate (partymanager.cpp:1503)
    }

    public static ByteWriter Error(PartyError error, string targetName = "") {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<PartyError>(error);
        pWriter.WriteUnicodeString(targetName);

        return pWriter;
    }

    public static ByteWriter Joined(PartyMember member) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Joined);
        pWriter.WriteClass(member);
        member.WriteDungeonEligibility(pWriter);

        return pWriter;
    }

    public static ByteWriter Leave(long targetCharacterId, bool isSelf) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Leave);
        pWriter.WriteLong(targetCharacterId);
        pWriter.WriteBool(isSelf);

        return pWriter;
    }

    public static ByteWriter Kicked(long targetCharacterId) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Kicked);
        pWriter.WriteLong(targetCharacterId);

        return pWriter;
    }

    public static ByteWriter NotifyLogin(PartyMember member) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.NotifyLogin);
        pWriter.WriteClass(member);

        return pWriter;
    }

    public static ByteWriter NotifyLogout(long targetCharacterId) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.NotifyLogout);
        pWriter.WriteLong(targetCharacterId);

        return pWriter;
    }

    public static ByteWriter Disband() {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Disbanded);

        return pWriter;
    }

    public static ByteWriter NotifyUpdateLeader(long newLeaderCharacterId) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.NotifyUpdateLeader);
        pWriter.WriteLong(newLeaderCharacterId);

        return pWriter;
    }

    public static ByteWriter Load(Party party, bool joinNotify = false) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteBool(joinNotify);
        pWriter.WriteInt(party.Id);
        pWriter.WriteLong(party.LeaderCharacterId);
        pWriter.WriteByte((byte) party.Members.Count);

        foreach (PartyMember member in party.Members.Values) {
            pWriter.WriteBool(!member.Info.Online);
            pWriter.WriteClass<PartyMember>(member);
            member.WriteDungeonEligibility(pWriter);
        }
        pWriter.WriteBool(party.DungeonSet);
        pWriter.WriteInt(party.DungeonId);
        pWriter.WriteByte(DungeonMatchRules.SearchBits(party.DungeonMatchKind)); // the match searching bits
        pWriter.WriteByte();
        pWriter.WriteBool(party.Search != null);
        if (party.Search != null) {
            pWriter.WriteClass<PartySearch>(party.Search);
        }

        return pWriter;
    }

    public static ByteWriter Invite(int partyId, string name) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Invited);

        pWriter.WriteUnicodeString(name);
        pWriter.WriteInt(partyId);

        return pWriter;
    }

    public static ByteWriter Update(PartyMember member) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.UpdateMember);

        pWriter.WriteLong(member.CharacterId);
        pWriter.WriteClass<PartyMember>(member);

        return pWriter;
    }

    public static ByteWriter UpdateDungeonInfo(PartyMember member) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.UpdateDungeonInfo);

        pWriter.WriteLong(member.CharacterId);
        member.WriteDungeonEligibility(pWriter);

        return pWriter;
    }

    public static ByteWriter Unknown1() {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.Unknown1);

        pWriter.WriteLong();
        pWriter.WriteInt();

        return pWriter;
    }

    // CPartyManager::FUN_14162e3d0 (partymanager.cpp:1255): the member's character id and a byte; the client prints
    // s_party_member_dead_tomb for 0 and s_party_member_dead_dark_tomb for the rest, and nothing about itself.
    public static ByteWriter Tombstone(long characterId, bool metal) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.WriteByte((byte) Command.Tombstone);

        pWriter.WriteLong(characterId);
        pWriter.WriteBool(metal);

        return pWriter;
    }

    public static ByteWriter UpdateStats(PartyMember member) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.UpdateStats);

        pWriter.WriteLong(member.CharacterId);
        pWriter.WriteLong(member.AccountId);
        pWriter.WriteInt((int) member.Info.TotalHp);
        pWriter.WriteInt((int) member.Info.CurrentHp);
        pWriter.Write<DeathState>(member.Info.DeathState);

        return pWriter;
    }

    public static ByteWriter PartyNotice(PartyMessage message, string systemSoundKey = "") {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.DungeonNotice);

        pWriter.WriteUnicodeString(message.ToString());
        // ReSharper disable once CommentTypo
        pWriter.WriteUnicodeString(systemSoundKey); // "Field_Enterance_Reset_Dungeon"
        pWriter.WriteUnicodeString();

        return pWriter;
    }

    // CPartyManager::OnPartyNoticeWithSound (partymanager.cpp:1281): an interface text, shown in chat type 2, then
    // the key of the system sound it plays.
    public static ByteWriter NoticeWithSound(InterfaceText text, string soundKey = "") {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.NoticeWithSound);
        pWriter.WriteClass<InterfaceText>(text);
        pWriter.WriteUnicodeString(soundKey);

        return pWriter;
    }

    public static ByteWriter DungeonReset(bool dungeonSet, int dungeonId) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.DungeonReset);
        pWriter.WriteBool(dungeonSet);
        pWriter.WriteInt(dungeonId);

        return pWriter;
    }

    // Client: CPartyManager::OnPartyDungeonRoomCreate (partymanager.cpp:1503): int dungeon id. A member who is not the
    // leader gets a yes/no box naming the dungeon; yes sends RoomDungeon 3 (enter the lobby). The leader's client ignores it.
    public static ByteWriter DungeonRoomCreate(int dungeonId) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.DungeonRoomCreate);
        pWriter.WriteInt(dungeonId);

        return pWriter;
    }

    public static ByteWriter LoadPartySearchListing(Party party) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.PartySearchListing);
        pWriter.WriteBool(party.Search != null);
        if (party.Search != null) {
            pWriter.WriteClass<PartySearch>(party.Search);
        }

        return pWriter;
    }

    // CPartyManager::OnPartyDungeonMatchRegister: the kind, registered or not, the notice flag, and the searching bits
    // the client keeps for IsMatchSearching.
    public static ByteWriter PartySearch(byte type, bool searching) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.PartySearch);
        // ReSharper disable once CommentTypo
        /*
            if type == 1:
                dungeon_message(1) # s_enum_dungeon_group_normal
            elif type == 2:
                dungeon_message(8) # s_enum_dungeon_group_worldBoss
            elif type == 3:
                dungeon_message(11) # s_enum_dungeon_group_event
            elif type == 4:
                dungeon_message(5) # s_enum_dungeon_group_lapenta
            else:
                dungeon_message(0)
        */
        pWriter.WriteByte(type); // Type
        pWriter.WriteBool(searching); // Searching
        pWriter.WriteBool(true); // always true?
        pWriter.WriteByte(searching ? DungeonMatchRules.SearchBits(type) : (byte) 0);

        return pWriter;
    }

    public static ByteWriter PartySearchDungeon() {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.PartySearchDungeon);
        // OnPartyDungeonMatchConfirm (partymanager.cpp:471) reads nothing; it shows s_party_match_dungeon.

        return pWriter;
    }

    public static ByteWriter DungeonRecord() {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.DungeonRecord);
        pWriter.WriteLong();

        return pWriter;
    }

    // CPartyManager::FUN_14162ee70 (partymanager.cpp:1357): a notice from a member, the character id, the name and an
    // interface text, shown in chat type 2. The client drops it when the name is blocked, or the name or the id is on
    // its buddy block list.
    public static ByteWriter MemberNotice(long characterId, string name, InterfaceText text) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.MemberNotice);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteClass<InterfaceText>(text);

        return pWriter;
    }

    // CPartyManager::OnPartyFindDungeonCooldown (partymanager.cpp:1425): the milliseconds left before the party may
    // recruit again (s_party_find_dungeon_helper_cooldown shows ms/1000+1 seconds).
    public static ByteWriter DungeonHelperCooldown(int milliseconds) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.DungeonHelperCooldown);
        pWriter.WriteInt(milliseconds);

        return pWriter;
    }

    public static ByteWriter JoinRequest(string name) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.JoinRequest);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    public static ByteWriter StartVote(PartyVote partyVote) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.StartVote);
        pWriter.WriteClass<PartyVote>(partyVote);

        return pWriter;
    }

    public static ByteWriter ReadyCheck(long characterId, bool isReady) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.ReadyCheck);
        pWriter.WriteLong(characterId);
        pWriter.WriteBool(isReady);

        return pWriter;
    }

    public static ByteWriter EndVote() {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.EndVote);

        return pWriter;
    }

    // CPartyManager::OnPartySurvivalSquadRegister: the party's Maple Survival squad is registered (the pass window's
    // squad mode, IsSurvivalSquadRegistered); notify: the register or unregister line, its sound and the survival state
    // refresh.
    public static ByteWriter SurvivalPartySearch(bool searching, bool notify = true) {
        var pWriter = Packet.Of(SendOp.Party);
        pWriter.Write<Command>(Command.SurvivalPartySearch);
        pWriter.WriteBool(searching);
        pWriter.WriteBool(notify);

        return pWriter;
    }
}
