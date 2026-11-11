using System;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class DungeonRoomRulesTests {
    [TestCase(false, 1, 0, 4, false, DungeonRoomRules.CreateVerdict.Ok)]
    [TestCase(false, 1, 4, 4, false, DungeonRoomRules.CreateVerdict.UnderMinUserCount)]
    [TestCase(false, 1, 4, 4, true, DungeonRoomRules.CreateVerdict.Ok)]
    [TestCase(true, 4, 0, 4, false, DungeonRoomRules.CreateVerdict.Ok)]
    [TestCase(true, 5, 0, 4, false, DungeonRoomRules.CreateVerdict.OverMaxUserCount)]
    [TestCase(true, 1, 0, 1, false, DungeonRoomRules.CreateVerdict.OverMaxUserCount)]
    [TestCase(true, 2, 4, 10, false, DungeonRoomRules.CreateVerdict.Ok)]
    public void Create(bool withParty, int partySize, int min, int max, bool gm, DungeonRoomRules.CreateVerdict expected) {
        Assert.That(DungeonRoomRules.CanCreate(withParty, partySize, min, max, gm), Is.EqualTo(expected));
    }

    // The guild raid rows of the played client's table: limitDayOfWeeks="1,2,3,4,6,7" (no Thursday).
    private static readonly DayOfWeek[] GuildRaidDays = [
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday,
    ];

    [TestCase(DayOfWeek.Sunday, true)]
    [TestCase(DayOfWeek.Wednesday, true)]
    [TestCase(DayOfWeek.Thursday, false)]
    [TestCase(DayOfWeek.Saturday, true)]
    public void OpenOnDay_TheListedDays(DayOfWeek today, bool open) {
        Assert.That(DungeonRoomRules.OpenOnDay(GuildRaidDays, today), Is.EqualTo(open));
    }

    [Test]
    public void OpenOnDay_NoListIsEveryDay() {
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) {
            Assert.That(DungeonRoomRules.OpenOnDay([], day), Is.True);
        }
    }

    // The ingest's day numbers are the client's: Sunday 1 to Saturday 7 (CMsTime::GetDayOfWeek = tm_wday + 1).
    [Test]
    public void ParserDayNumbersAreTheClients() {
        Assert.That((int) Maple2.File.Parser.Enum.DayOfWeek.sun, Is.EqualTo(1));
        Assert.That((int) Maple2.File.Parser.Enum.DayOfWeek.thu, Is.EqualTo(5));
        Assert.That((int) Maple2.File.Parser.Enum.DayOfWeek.sat, Is.EqualTo(7));
    }

    [Test]
    public void CreateBytes() {
        // u8 mode is read by the handler; then i32 dungeon id, bool party, i32 portal id, bool ticket.
        var writer = new ByteWriter();
        writer.WriteInt(23000001);
        writer.WriteBool(true);
        writer.WriteInt(7);
        writer.WriteBool(false);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
