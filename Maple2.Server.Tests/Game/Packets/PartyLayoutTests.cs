using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Party (0x53) layouts, read the way CPartyManager::OnParty (client/ms2/partymanager.cpp:447) reads each mode.
public class PartyLayoutTests {
    private static PartyMember Member(long accountId, long characterId, string name) {
        var info = new PlayerInfo(new CharacterInfo(accountId, characterId, name, "", "", Gender.Female, Job.Knight, 42), "", new AchievementInfo(), [77]) {
            Channel = 3,
            MapId = 2000062,
            RoomId = 9,
            TotalHp = 5000,
            CurrentHp = 4000,
            GearScore = 1234,
            GuildId = 55,
        };
        info.DungeonEnterLimits[23000001] = DungeonEnterLimit.MinLevel;
        return new PartyMember { Info = info };
    }

    // The member block: Unk_UserProxy::FUN_1408eeb60 (commonfiles/userproxy.cpp:1368), with the KR platform proxy's
    // vfunc_6 (userproxy.cpp:279, a string and an int64) after the 12 mastery ints.
    private static void ReadMember(ByteReader reader, long accountId, long characterId, string name) {
        Assert.That(reader.ReadLong(), Is.EqualTo(accountId));
        Assert.That(reader.ReadLong(), Is.EqualTo(characterId));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(name));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) Gender.Female)); // m_14
        reader.ReadByte(); // m_78, the GM grade
        Assert.That(reader.ReadLong(), Is.EqualTo(accountId)); // m_18
        reader.ReadInt(); // m_20
        Assert.That(reader.ReadInt(), Is.EqualTo(2000062)); // m_28
        Assert.That(reader.ReadInt(), Is.EqualTo(2000062)); // m_2c
        Assert.That(reader.ReadInt(), Is.EqualTo(9)); // m_30
        Assert.That(reader.ReadShort(), Is.EqualTo(42)); // m_34 level
        Assert.That(reader.ReadShort(), Is.EqualTo(3)); // m_38 channel
        Assert.That(reader.ReadInt(), Is.EqualTo((int) JobCode.Knight)); // m_3c
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Knight)); // m_40
        Assert.That(reader.ReadInt(), Is.EqualTo(5000)); // m_44
        Assert.That(reader.ReadInt(), Is.EqualTo(4000)); // m_48
        reader.ReadShort(); // m_4c, the death state
        reader.ReadLong(); // m_50
        reader.ReadLong(); // m_b0
        reader.ReadLong(); // m_b8
        reader.ReadInt(); // m_c0
        reader.ReadFloat(); // m_c4
        reader.ReadFloat();
        reader.ReadFloat();
        Assert.That(reader.ReadInt(), Is.EqualTo(1234)); // m_d0 gear score
        reader.ReadInt(); // m_58[0..1], the skin color
        reader.ReadInt();
        reader.ReadLong(); // m_70
        reader.ReadInt(); // m_d4, three ints
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(55)); // m_80 guild id
        reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        // DecodeClubs -> Club::UserClub::FUN_14020cfc0 (clubcommon.cpp:248): u8 count, then u8 flag (1 = id and name).
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        reader.ReadUnicodeString();
        reader.ReadByte(); // m_24
        for (int i = 0; i < 12; i++) {
            reader.ReadInt(); // m_140
        }
        reader.ReadUnicodeString(); // KR platform proxy
        reader.ReadLong();
        Assert.That(reader.ReadInt(), Is.EqualTo(0)); // set count
        reader.ReadByte(); // m_180
        reader.ReadBool(); // m_184
        reader.ReadLong(); // m_188
        reader.ReadInt(); // m_190
        reader.ReadInt(); // m_194
        reader.ReadLong(); // m_198
        reader.ReadInt(); // m_1a8
        reader.ReadLong(); // m_1b0
        Assert.That(reader.ReadInt(), Is.EqualTo(0)); // m_1b8 count
        Assert.That(reader.ReadInt(), Is.EqualTo(0)); // m_1e8 count
        reader.ReadShort(); // m_1ac
        reader.ReadLong(); // m_88
    }

    // Unk_LockedIntMap::Read (unnamed/unit_140268010_sharedstatemap.cpp:85): int count, then (int id, u8 limit) pairs.
    private static void ReadCounters(ByteReader reader) {
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DungeonEnterLimit.MinLevel));
    }

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    // Mode 2, OnPartyMemberJoin: the member block, then the counters.
    [Test]
    public void JoinedIsMemberThenCounters() {
        ByteWriter writer = PartyPacket.Joined(Member(10, 11, "ab"));
        ByteReader reader = Open(writer, 2);

        ReadMember(reader, 10, 11, "ab");
        ReadCounters(reader);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 5, OnPartyMemberOnline (partymanager.cpp:1162): the member block alone.
    [Test]
    public void NotifyLoginIsMemberOnly() {
        ByteWriter writer = PartyPacket.NotifyLogin(Member(10, 11, "ab"));
        ByteReader reader = Open(writer, 5);

        ReadMember(reader, 10, 11, "ab");
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 0xc, OnPartyMemberInfoUpdate: the character id, then the member block.
    [Test]
    public void UpdateIsIdThenMember() {
        ByteWriter writer = PartyPacket.Update(Member(10, 11, "ab"));
        ByteReader reader = Open(writer, 0xc);

        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        ReadMember(reader, 10, 11, "ab");
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 0xe, OnPartyMemberCounters: the character id, then the counters.
    [Test]
    public void UpdateDungeonInfoIsIdThenCounters() {
        ByteWriter writer = PartyPacket.UpdateDungeonInfo(Member(10, 11, "ab"));
        ByteReader reader = Open(writer, 0xe);

        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        ReadCounters(reader);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 0x13, FUN_14162dfc0: the character id, then Unk_UserProxy::DecodeFlags (Unk_UserProxyFlagsPacket::Decode,
    // userproxy.cpp:838): int64, int, int, short.
    [Test]
    public void UpdateStatsIsIdThenFlags() {
        ByteWriter writer = PartyPacket.UpdateStats(Member(10, 11, "ab"));
        ByteReader reader = Open(writer, 0x13);

        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(10));
        Assert.That(reader.ReadInt(), Is.EqualTo(5000));
        Assert.That(reader.ReadInt(), Is.EqualTo(4000));
        reader.ReadShort();
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 9, OnPartyInfo (partymanager.cpp:1104): bool, int party id, int64 leader, u8 count, per member (bool
    // offline, member block, counters), then bool, int dungeon id, u8 bits, bool, u8 has-recruit and the recruit.
    [Test]
    public void LoadIsHeaderMembersThenTail() {
        PartyMember leader = Member(10, 11, "ab");
        var party = new Party(4, leader) {
            DungeonSet = true,
            DungeonId = 23000001,
            Search = new PartySearch(99, "s", 4) { LeaderName = "ab", PartyId = 4 },
        };
        party.Members[leader.CharacterId] = leader;
        ByteWriter writer = PartyPacket.Load(party, true);
        ByteReader reader = Open(writer, 9);

        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.False); // online
        ReadMember(reader, 10, 11, "ab");
        ReadCounters(reader);
        Assert.That(reader.ReadBool(), Is.True); // matchmaking (Read0)
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001)); // Read6
        reader.ReadByte(); // Unk_PartyBits, one byte (dungeonrecord.cpp:46)
        reader.ReadBool(); // flag29
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        // CMatchPartyRecruit::Read -> FUN_140694dc0 (commonfiles/matchpartyrecruitschcond.cpp:252).
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("s"));
        reader.ReadByte(); // allowAutoJoin
        reader.ReadInt();
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        reader.ReadLong();
        reader.ReadLong(); // the leader's character id: the list's "mine" test (uipartysearchdialog.cpp:289)
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        reader.ReadLong();
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 0, OnPartyError: u8 error, then the target name.
    [Test]
    public void ErrorIsByteThenName() {
        ByteWriter writer = PartyPacket.Error(PartyError.s_party_err_full, "ab");
        ByteReader reader = Open(writer, 0);

        Assert.That(reader.ReadByte(), Is.EqualTo((byte) PartyError.s_party_err_full));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The fixed-shape modes: their total size after the opcode and the mode byte.
    [Test]
    public void FixedModesHaveTheClientSizes() {
        Assert.Multiple(() => {
            Assert.That(PartyPacket.Leave(1, true).Length, Is.EqualTo(3 + 8 + 1));
            Assert.That(PartyPacket.Kicked(1).Length, Is.EqualTo(3 + 8));
            Assert.That(PartyPacket.NotifyLogout(1).Length, Is.EqualTo(3 + 8));
            Assert.That(PartyPacket.Disband().Length, Is.EqualTo(3));
            Assert.That(PartyPacket.NotifyUpdateLeader(1).Length, Is.EqualTo(3 + 8));
            Assert.That(PartyPacket.Invite(1, "ab").Length, Is.EqualTo(3 + 2 + 4 + 4));
            Assert.That(PartyPacket.Unknown1().Length, Is.EqualTo(3 + 8 + 4));
            Assert.That(PartyPacket.Tombstone(1, true).Length, Is.EqualTo(3 + 8 + 1));
            Assert.That(PartyPacket.DungeonReset(true, 1).Length, Is.EqualTo(3 + 1 + 4));
            Assert.That(PartyPacket.PartySearch(1, true).Length, Is.EqualTo(3 + 1 + 1 + 1 + 1));
            Assert.That(PartyPacket.PartySearchDungeon().Length, Is.EqualTo(3));
            Assert.That(PartyPacket.DungeonRecord().Length, Is.EqualTo(3 + 8));
            Assert.That(PartyPacket.DungeonHelperCooldown(1).Length, Is.EqualTo(3 + 4));
            Assert.That(PartyPacket.JoinRequest("ab").Length, Is.EqualTo(3 + 2 + 4));
            Assert.That(PartyPacket.ReadyCheck(1, true).Length, Is.EqualTo(3 + 8 + 1));
            Assert.That(PartyPacket.EndVote().Length, Is.EqualTo(3));
            Assert.That(PartyPacket.SurvivalPartySearch(true).Length, Is.EqualTo(3 + 1 + 1));
        });
    }

    // Mode 0x1a, OnPartyRecruitUpdate: u8 has-recruit; none here.
    [Test]
    public void SearchListingWithoutSearchIsOneByte() {
        var party = new Party(4, Member(10, 11, "ab"));
        ByteWriter writer = PartyPacket.LoadPartySearchListing(party);
        ByteReader reader = Open(writer, 0x1a);

        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}
