using System.Collections.Generic;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The client's fame product check (FUN_141b04af0, client/ms2/uishopdialog.cpp:3612) and the alliance code a shop
// product carries (npcshopproductbase.cpp:167, the client's fame alliance numbering).
public class FameShopRulesTests {
    // The KR grade points of the five first alliances (2: 10000, 3: 30000, 4: 60000, 5: 120000).
    private static FameTable Table() {
        var grades = new List<FameTable.Grade>();
        for (int alliance = 1; alliance <= 5; alliance++) {
            grades.Add(new FameTable.Grade((AllianceType) alliance, 1, 0));
            grades.Add(new FameTable.Grade((AllianceType) alliance, 2, 10000));
            grades.Add(new FameTable.Grade((AllianceType) alliance, 3, 30000));
            grades.Add(new FameTable.Grade((AllianceType) alliance, 4, 60000));
            grades.Add(new FameTable.Grade((AllianceType) alliance, 5, 120000));
        }
        return new FameTable([], new Dictionary<int, AllianceType>(), grades);
    }

    private static ShopItemMetadata.QuestAlliance Need(AllianceType alliance, int grade) => new(alliance, grade);

    [Test]
    public void NoGrade_NeedsNothing() {
        Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.AnyFaction, 0), Table(), new Dictionary<AllianceType, int>()), Is.True);
        Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.MapleAlliance, 0), Table(), new Dictionary<AllianceType, int>()), Is.True);
    }

    [Test]
    public void Grade_IsTheAlliancesOwn() {
        var points = new Dictionary<AllianceType, int> { [AllianceType.MapleAlliance] = 29999, [AllianceType.GreenHoods] = 200000 };
        Assert.Multiple(() => {
            Assert.That(ShopRules.FameGrade(Table(), points, AllianceType.MapleAlliance), Is.EqualTo(2));
            Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.MapleAlliance, 3), Table(), points), Is.False);
            Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.MapleAlliance, 2), Table(), points), Is.True);
            Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.DarkWind, 2), Table(), points), Is.False);
        });

        points[AllianceType.MapleAlliance] = 30000;
        Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.MapleAlliance, 3), Table(), points), Is.True);
    }

    [Test]
    public void AnyAlliance_TakesTheBestGrade() {
        var points = new Dictionary<AllianceType, int> { [AllianceType.DarkWind] = 10000, [AllianceType.RoyalGuard] = 60000 };
        Assert.Multiple(() => {
            Assert.That(ShopRules.FameGrade(Table(), points, AllianceType.AnyFaction), Is.EqualTo(4));
            Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.AnyFaction, 4), Table(), points), Is.True);
            Assert.That(ShopRules.CheckFameProduct(Need(AllianceType.AnyFaction, 5), Table(), points), Is.False);
        });
    }

    [Test]
    public void AllianceWithoutGrades_IsGradeZero() {
        var points = new Dictionary<AllianceType, int> { [(AllianceType) 9] = 500000 };
        Assert.That(ShopRules.CheckFameProduct(Need((AllianceType) 9, 2), Table(), points), Is.False);
    }

    // The shop table's requireAlliance names, in the numbering the client reads the product's alliance in.
    [TestCase("DarkWind", 1)]
    [TestCase("MapleUnion", 2)]
    [TestCase("LumiKnight", 3)]
    [TestCase("TriaRoyalGuard", 4)]
    [TestCase("GreenHood", 5)]
    [TestCase("MapleUnion_KritiasExped", 6)]
    [TestCase("Lumiknight_KritiasExped", 7)]
    [TestCase("GreenHood_KritiasExped", 8)]
    [TestCase("Georg", 9)]
    [TestCase("", 0)]
    public void ShopAllianceName_IsTheClientsFameAlliance(string name, int code) {
        Assert.That((int) TableMapper.ParseAlliance(name), Is.EqualTo(code));
    }
}
