using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Widget;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

public class OxQuizPickTests {
    private static OxQuizTable.Entry Question(int id, int level) => new(id, 1, "c", "q" + id, level, id % 2 == 0, "a");

    private static readonly OxQuizTable.Entry[] Table = [
        Question(1, 1), Question(2, 1), Question(3, 1), Question(4, 2), Question(5, 2),
    ];

    [Test]
    public void PicksOnlyTheLevel() {
        var random = new Random(7);
        for (int i = 0; i < 50; i++) {
            Assert.That(OxQuizWidget.Pick(Table, 2, new HashSet<int>(), random)!.Level, Is.EqualTo(2));
        }
    }

    [Test]
    public void SkipsAskedQuestions() {
        var random = new Random(7);
        var asked = new HashSet<int>();
        for (int i = 0; i < 3; i++) {
            OxQuizTable.Entry? question = OxQuizWidget.Pick(Table, 1, asked, random);
            Assert.That(question, Is.Not.Null);
            Assert.That(asked.Add(question!.Id), Is.True, "question asked twice");
        }
        Assert.That(asked, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void LevelUsedUpPicksAgain() {
        OxQuizTable.Entry? question = OxQuizWidget.Pick(Table, 2, new HashSet<int> { 4, 5 }, new Random(7));
        Assert.That(question?.Id, Is.AnyOf(4, 5));
    }

    [Test]
    public void LevelWithoutQuestionsPicksNone() {
        Assert.That(OxQuizWidget.Pick(Table, 3, new HashSet<int>(), new Random(7)), Is.Null);
        Assert.That(OxQuizWidget.Pick(Enumerable.Empty<OxQuizTable.Entry>(), 1, new HashSet<int>(), new Random(7)), Is.Null);
    }
}
