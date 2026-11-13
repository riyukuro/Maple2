using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// Decoration exp as CMasteryInteriorTable::AddMastery adds it (masteryinteriortable.cpp:247): the exp is into the
// current grade, a grade reached starts again from 0 with the rest carried on, and at the top grade none is kept.
// The grade values are the KR masteryUGCHousing ones (grades 1-4: 0, 100, 300, 1000).
public class HomeDecorationExpTests {
    private static readonly Dictionary<int, MasteryUgcHousingTable.Entry> Table = new() {
        [1] = new(1, 0, 0),
        [2] = new(2, 100, 20301019),
        [3] = new(3, 300, 20301020),
        [4] = new(4, 1000, 20301021),
    };

    private static (long Level, long Exp) After(long level, long exp, long amount) {
        var home = new Home { DecorationLevel = level, DecorationExp = exp };
        home.GainExp(amount, Table);
        return (home.DecorationLevel, home.DecorationExp);
    }

    [TestCase(1, 0, 50, 1, 50)]
    [TestCase(1, 50, 100, 2, 50)] // 150 reaches grade 2 (100); 50 is left into grade 2
    [TestCase(1, 0, 100, 2, 0)]
    [TestCase(1, 0, 450, 3, 50)] // 100 to grade 2, 300 to grade 3, 50 left
    [TestCase(2, 250, 60, 3, 10)]
    [TestCase(3, 900, 200, 4, 0)] // the top grade keeps no exp
    [TestCase(4, 0, 500, 4, 0)]
    [TestCase(0, 0, 30, 1, 30)] // grade 1 takes 0, so a home at 0 reaches it at once
    [TestCase(1, 20, 0, 1, 20)]
    public void Gain(long level, long exp, long amount, long expectedLevel, long expectedExp) {
        Assert.That(After(level, exp, amount), Is.EqualTo((expectedLevel, expectedExp)));
    }
}
