using System;
using System.Linq;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// QuestGroupPick against vectors computed by a C copy of the client's own code (QuestDataTable::FUN_14074e6c0,
// Unk_Random of questmasktable.cpp:373-461, GetWeekOfYear and NextWeekdayStart of timeutil.cpp), not by this port.
// Each pick is given as an index into the group's ids in id order.
public class QuestGroupPickTests {
    [TestCase(1, new uint[] { 4021160842, 1726117639, 3005936194, 398705183, 543018402 })]
    [TestCase(-5, new uint[] { 4063212470, 2436362868, 598228965, 2754252062, 1238300179 })]
    [TestCase(0, new uint[] { 513019773, 3764486746, 2941395024, 3755224129, 1070416350 })]
    public void ClientRandom_Sequence(int seed, uint[] expected) {
        var random = new QuestGroupPick.ClientRandom(seed);
        uint[] values = Enumerable.Range(0, expected.Length).Select(_ => random.Next()).ToArray();
        Assert.That(values, Is.EqualTo(expected));
    }

    [TestCase(2, 7300, 2026, 11, 12, 1877229536)]
    [TestCase(2, 7301, 2026, 11, 12, 1897490648)]
    [TestCase(2, 8005, 2026, 11, 12, -1018555688)] // wraps
    [TestCase(2, 8006, 2027, 1, 1, -926328642)]
    [TestCase(2, 91000000, 2026, 11, 12, 451303936)]
    [TestCase(2, 91000000, 2028, 2, 29, 636549056)]
    [TestCase(2, 3998, 2026, 12, 31, -599977086)]
    [TestCase(3, 7302, 2026, 11, 12, 1909967828)] // week of Fri 2026-11-13: 46
    [TestCase(3, 7302, 2026, 11, 13, 1909975130)] // on the Friday: the next one, week 47
    [TestCase(3, 7305, 2026, 12, 31, 2043469241)] // next Friday is in 2027, week 1
    [TestCase(3, 94000330, 2026, 11, 14, -1819740330)]
    [TestCase(3, 94000330, 2027, 6, 6, -281318448)]
    [TestCase(3, 1, 2026, 1, 2, 20260002)]
    [TestCase(3, 1, 2027, 1, 7, 20270002)]
    [TestCase(3, 1, 2028, 12, 28, 20280053)]
    [TestCase(3, 1, 2023, 1, 5, 20230002)]
    [TestCase(1, 7300, 2026, 11, 12, 0)]
    [TestCase(0, 7300, 2026, 11, 12, 0)]
    public void Seed(int repeat, int groupId, int year, int month, int day, int expected) {
        Assert.That(QuestGroupPick.Seed(repeat, groupId, new DateTime(year, month, day, 12, 0, 0)), Is.EqualTo(expected));
    }

    // The time of day does not move a daily seed.
    [Test]
    public void Seed_DailyIsTheDate() {
        Assert.That(QuestGroupPick.Seed(2, 7300, new DateTime(2026, 11, 12, 0, 0, 0)),
            Is.EqualTo(QuestGroupPick.Seed(2, 7300, new DateTime(2026, 11, 12, 23, 59, 59))));
    }

    [TestCase(2026, 11, 13, 46)]
    [TestCase(2026, 11, 20, 47)]
    [TestCase(2027, 1, 1, 1)]
    [TestCase(2027, 6, 11, 24)]
    [TestCase(2026, 1, 9, 2)]
    [TestCase(2027, 1, 8, 2)]
    [TestCase(2028, 12, 29, 53)]
    [TestCase(2023, 1, 6, 2)]
    public void WeekOfYear(int year, int month, int day, int expected) {
        Assert.That(QuestGroupPick.WeekOfYear(year, month, day), Is.EqualTo(expected));
    }

    [TestCase(2, 7300, 2026, 11, 12, 18, 1, new[] { 11 })]
    [TestCase(2, 7301, 2026, 11, 12, 18, 1, new[] { 8 })]
    [TestCase(2, 8005, 2026, 11, 12, 22, 1, new[] { 7 })]
    [TestCase(2, 8006, 2027, 1, 1, 22, 1, new[] { 9 })]
    [TestCase(2, 91000000, 2026, 11, 12, 106, 1, new[] { 15 })]
    [TestCase(2, 91000000, 2028, 2, 29, 106, 1, new[] { 84 })]
    [TestCase(2, 3998, 2026, 12, 31, 10, 1, new[] { 7 })]
    [TestCase(2, 7300, 2026, 11, 13, 18, 3, new[] { 16, 4, 10 })]
    [TestCase(3, 7302, 2026, 11, 12, 5, 1, new[] { 2 })]
    [TestCase(3, 7302, 2026, 11, 13, 5, 1, new[] { 2 })]
    [TestCase(3, 7305, 2026, 12, 31, 5, 1, new[] { 3 })]
    [TestCase(3, 94000330, 2026, 11, 14, 3, 1, new[] { 0 })]
    [TestCase(3, 94000330, 2027, 6, 6, 3, 2, new[] { 2, 1 })]
    public void Pick(int repeat, int groupId, int year, int month, int day, int count, int picks, int[] expected) {
        int[] ids = Enumerable.Range(0, count).Select(i => 1000 + i).ToArray();
        var now = new DateTime(year, month, day, 12, 0, 0);
        Assert.That(QuestGroupPick.Pick(ids, repeat, groupId, now, picks), Is.EqualTo(expected.Select(i => 1000 + i).ToArray()));
    }

    [Test]
    public void Pick_EmptyGroup() {
        Assert.That(QuestGroupPick.Pick([], 2, 7300, new DateTime(2026, 11, 12)), Is.Empty);
    }
}
