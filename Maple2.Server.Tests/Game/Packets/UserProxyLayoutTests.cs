using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// A party member's user data (PlayerInfo.WriteTo) is read by Unk_UserProxy::FUN_1408eeb60 (userproxy.cpp:1367), the
// party's Join (0x02), Online (0x05), Info (0x09) and member update (0x0c) readers (partymanager.cpp). After the 12
// mastery ints the region's platform proxy reads its own part (CKRPlatformProxy::vfunc_6 userproxy.cpp:279, JP :364,
// CN :476, NA :629), so the fields after it shift unless the server writes that region's layout.
public class UserProxyLayoutTests {
    // The client reader's field sequence; returns the level, the guild id and the last long.
    private static (short Level, long GuildId, long Last) ReadUser(ByteReader reader, string locale) {
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadUnicodeString();
        reader.ReadByte();
        reader.ReadByte();
        reader.ReadLong();
        for (int i = 0; i < 4; i++) reader.ReadInt();
        short level = reader.ReadShort();
        reader.ReadShort();
        for (int i = 0; i < 4; i++) reader.ReadInt();
        reader.ReadShort();
        for (int i = 0; i < 3; i++) reader.ReadLong();
        reader.ReadInt();
        for (int i = 0; i < 3; i++) reader.ReadFloat();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadLong();
        for (int i = 0; i < 3; i++) reader.ReadInt();
        long guildId = reader.ReadLong();
        reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        byte clubs = reader.ReadByte();
        for (int i = 0; i < clubs; i++) {
            if (reader.ReadByte() == 1) {
                reader.ReadLong();
                reader.ReadUnicodeString();
            }
        }
        reader.ReadByte();
        for (int i = 0; i < 12; i++) reader.ReadInt();
        ReadPlatform(reader, locale);
        int sets = reader.ReadInt();
        for (int i = 0; i < sets; i++) reader.ReadLong();
        reader.ReadByte();
        reader.ReadBool();
        reader.ReadLong();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadLong();
        reader.ReadInt();
        reader.ReadLong();
        for (int list = 0; list < 2; list++) {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++) reader.ReadLong();
        }
        reader.ReadShort();
        long last = reader.ReadLong();
        return (level, guildId, last);
    }

    internal static void ReadPlatform(ByteReader reader, string locale) {
        switch (locale) {
            case "NA":
                reader.ReadUnicodeString();
                reader.ReadLong();
                reader.ReadLong();
                reader.ReadLong();
                break;
            case "CN":
                reader.ReadInt();
                reader.ReadLong();
                reader.ReadByte();
                reader.ReadInt();
                break;
            default:
                reader.ReadUnicodeString();
                reader.ReadLong();
                break;
        }
    }

    private static PartyMember Member() {
        var info = new PlayerInfo(new CharacterInfo(11, 22, "a", "", "", Gender.Female, Job.Newbie, 37), "", new AchievementInfo(), [5, 6]) {
            GuildId = 9876,
        };
        return new PartyMember { Info = info };
    }

    [TestCase("KR", 2 + 8)]
    [TestCase("JP", 2 + 8)]
    [TestCase("NA", 2 + 24)]
    [TestCase("CN", 4 + 8 + 1 + 4)]
    [TestCase("TW", 2 + 8)]
    public void Platform_ByRegion(string locale, int size) {
        var writer = new ByteWriter();
        PlatformBlock.Write(writer, locale);
        Assert.That(writer.Length, Is.EqualTo(size));
        var reader = new ByteReader(writer.ToArray(), 0);
        ReadPlatform(reader, locale);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Online_ReadsTheWholeUser() {
        var reader = new ByteReader(PartyPacket.NotifyLogin(Member()).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        (short level, long guildId, _) = ReadUser(reader, Target.LOCALE);
        Assert.That(level, Is.EqualTo(37));
        Assert.That(guildId, Is.EqualTo(9876));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Info: bool announce, int party id, long leader, u8 count; per member bool offline, the user, the counters
    // (Unk_LockedIntMap::Read: int count x (int, u8)); then bool, u32 dungeon, u8 bits, bool, u8 has recruit.
    [Test]
    public void Info_ReadsTheMembersAndTheTail() {
        PartyMember member = Member();
        var party = new Party(1, member);
        party.Members.TryAdd(member.CharacterId, member);
        var reader = new ByteReader(PartyPacket.Load(party).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(9));
        reader.ReadBool();
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        reader.ReadBool();
        Assert.That(ReadUser(reader, Target.LOCALE).GuildId, Is.EqualTo(9876));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        reader.ReadBool();
        reader.ReadInt();
        reader.ReadByte();
        reader.ReadBool();
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
