using Maple2.Model.Enum;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class CommonExpRulesTests {
    // Guild check-in exp is read from the curve by the member's own level.
    [TestCase(ExpType.guildUserExp, CommonExpRules.LevelSource.Character)]
    [TestCase(ExpType.fishing, CommonExpRules.LevelSource.Character)]
    [TestCase(ExpType.userMiniGame, CommonExpRules.LevelSource.Character)]
    [TestCase(ExpType.petTaming, CommonExpRules.LevelSource.Character)]
    // The arenas' results (before: "Unhandled ExpType", no exp).
    [TestCase(ExpType.redDuelLose, CommonExpRules.LevelSource.Character)]
    [TestCase(ExpType.bloodMineRank1, CommonExpRules.LevelSource.Character)]
    [TestCase(ExpType.taxi, CommonExpRules.LevelSource.Field)]
    [TestCase(ExpType.rareChestFirst, CommonExpRules.LevelSource.Field)]
    [TestCase(ExpType.none, CommonExpRules.LevelSource.None)]
    public void Level(ExpType expType, CommonExpRules.LevelSource expected) {
        Assert.That(CommonExpRules.Level(expType), Is.EqualTo(expected));
    }

    [TestCase(30, 50, 30)]
    [TestCase(70, 50, 50)]
    public void FieldLevel(int fieldLevel, int characterLevel, int expected) {
        Assert.That(CommonExpRules.FieldLevel(fieldLevel, characterLevel), Is.EqualTo(expected));
    }

    // Made-up numbers: curve value 1000, guild rate 2, type factor 0.25.
    [Test]
    public void Amount() {
        Assert.That(CommonExpRules.Amount(1000, 2f, 0.25f, 0), Is.EqualTo(500));
        Assert.That(CommonExpRules.Amount(1000, 1f, 0.25f, 7), Is.EqualTo(257));
        Assert.That(CommonExpRules.Amount(0, 2f, 0.25f, 0), Is.EqualTo(0));
    }
}
