using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Club;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ClubBuffTests {
    private static readonly (int, int)[] Rows = [(110002001, 1), (110002002, 1), (110002003, 1)];

    // The request, mode 13 after the mode byte: i64 club id, i32 effect id, i32 level.
    [Test]
    public void RequestBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(0x0102030405060708);
        writer.WriteInt(110002002);
        writer.WriteInt(1);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(ClubRules.ReadBuff(reader), Is.EqualTo((0x0102030405060708, 110002002, 1)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(110002001, 1, 0, 0, true)]
    [TestCase(110002003, 1, 110002001, 1, true)]
    [TestCase(110002001, 1, 110002001, 1, false)] // the club has it already
    [TestCase(110002001, 2, 0, 0, false)] // not a row's level
    [TestCase(110002004, 1, 0, 0, false)] // not a row
    [TestCase(0, 0, 110002001, 1, false)] // the window never clears the buff
    [TestCase(0, 0, 0, 0, false)]
    public void OnlyATableRowIsSet(int buffId, int buffLevel, int currentId, int currentLevel, bool expected) {
        Assert.That(ClubRules.BuffAllowed(Rows, buffId, buffLevel, currentId, currentLevel), Is.EqualTo(expected));
    }

    // Only the effect id is stored; the row gives the level, and an id with no row is no buff.
    [Test]
    public void StoredBuffLevel() {
        Assert.That(ClubRules.StoredBuff(Rows, 110002002), Is.EqualTo((110002002, 1)));
        Assert.That(ClubRules.StoredBuff(Rows, 110002009), Is.EqualTo((0, 0)));
        Assert.That(ClubRules.StoredBuff(Rows, 0), Is.EqualTo((0, 0)));
        Assert.That(ClubRules.StoredBuff([(0, 0)], 0), Is.EqualTo((0, 0)));
    }

    // Mode 22: i64 club id, i32 effect id, i32 level.
    [Test]
    public void ChangeBuffBytes() {
        ByteWriter writer = ClubPacket.ChangeBuff(0x0102030405060708, 110002003, 1);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
        Assert.That(reader.ReadByte(), Is.EqualTo(22));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.ReadInt(), Is.EqualTo(110002003));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The club block: the buff id and level sit between the state byte and the rename time.
    [Test]
    public void ClubBlockCarriesTheBuff() {
        var leader = new ClubMember {
            Info = new PlayerInfo(new CharacterInfo(11, 22, "Lead", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []),
            ClubId = 5,
        };
        var club = new Club(5, "Name", leader) { CreationTime = 99, BuffId = 110002001, BuffLevel = 1, NameChangeCooldown = 77 };
        var writer = new ByteWriter();
        club.WriteTo(writer);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Name"));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Lead"));
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        reader.ReadByte();
        Assert.That(reader.ReadInt(), Is.EqualTo(110002001));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The table's effect ids carry a space after the number.
    [Test]
    public void TableRows() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <clubBuff id="1" additionalEffectId="9001 " additionalEffectLevel="1" />
              <clubBuff id="2" additionalEffectId="9002 " additionalEffectLevel="3" />
            </ms2>
            """);
        Dictionary<int, ClubBuffTable.Entry> rows = TableMapper.ReadClubBuffs(document);
        Assert.That(rows.Keys, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(rows[2], Is.EqualTo(new ClubBuffTable.Entry(2, 9002, 3)));
    }
}
