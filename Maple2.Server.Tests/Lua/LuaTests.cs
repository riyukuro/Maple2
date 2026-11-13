using Maple2.Model.Game;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Lua;

// Every test runs once per locale branch: the script (Maple2.Lua) and the port are both set to the
// fixture's locale and both must give the expected value, whatever LOCALE the test run has.
[NonParallelizable]
[TestFixture("NA")]
[TestFixture("KR")]
public class LuaTests {
    private readonly string locale;
    private readonly Maple2.Lua.Lua maple2Lua;
    private string portLocale = string.Empty;

    public LuaTests(string locale) {
        this.locale = locale;
        maple2Lua = new Maple2.Lua.Lua(locale);
    }

    [OneTimeSetUp]
    public void OneTimeSetUp() {
        portLocale = Server.Game.LuaFunctions.Lua.Locale;
        Server.Game.LuaFunctions.Lua.Locale = locale;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() {
        Server.Game.LuaFunctions.Lua.Locale = portLocale;
        maple2Lua.Dispose();
    }

    // The expected value of the fixture's branch.
    private T V<T>(T na, T kr) => locale == "NA" ? na : kr;

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringThrowingStar() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13460307); // Exceptional Adventuring Throwing Star

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(V(4177, 685)));
            Assert.That(baseGearScore, Is.EqualTo(V(4177, 685)));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(V(0, 0)));
            Assert.That(enchantScore, Is.EqualTo(V(0, 0)));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(V(4177, 685)));
            Assert.That(totalGearScore, Is.EqualTo(V(4177, 685)));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringThrowingStar_Enchanted() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13460307); // Exceptional Adventuring Throwing Star

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(V(4178, 685)));
            Assert.That(baseGearScore, Is.EqualTo(V(4178, 685)));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(V(2088, 140)));
            Assert.That(enchantScore, Is.EqualTo(V(2088, 140)));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(V(6266, 825)));
            Assert.That(totalGearScore, Is.EqualTo(V(6266, 825)));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringKnife() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13160314); // Exceptional Adventuring Knife

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(V(4178, 685)));
            Assert.That(baseGearScore, Is.EqualTo(V(4178, 685)));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(V(2088, 140)));
            Assert.That(enchantScore, Is.EqualTo(V(2088, 140)));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(V(6266, 825)));
            Assert.That(totalGearScore, Is.EqualTo(V(6266, 825)));
        });
    }

    [Test]
    public void TestCalcItemLevel_RookStar() {
        // Arrange
        const int gearScore = 7;
        const int rarity = 1;
        var itemType = new ItemType(13400218); // Rook's Star

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(V(35, 70)));
            Assert.That(baseGearScore, Is.EqualTo(V(35, 70)));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(V(0, 0)));
            Assert.That(enchantScore, Is.EqualTo(V(0, 0)));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(V(35, 70)));
            Assert.That(totalGearScore, Is.EqualTo(V(35, 70)));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringCodex() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(14060270); // Exceptional Adventuring Codex

        // Act
        int maple2LuaResult = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0).Item1;
        int luaResult = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0).Item1;

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(V(8355, 1370)));
            Assert.That(luaResult, Is.EqualTo(V(8355, 1370)));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringScepter() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13360308); // Exceptional Adventuring Scepter

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(V(8355, 1370)));
            Assert.That(baseGearScore, Is.EqualTo(V(8355, 1370)));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(V(4177, 280)));
            Assert.That(enchantScore, Is.EqualTo(V(4177, 280)));

            // total
            Assert.That(maple2LuaBaseGearScore + maple2LuaEnchantScore, Is.EqualTo(V(12532, 1650)));
            Assert.That(baseGearScore + enchantScore, Is.EqualTo(V(12532, 1650)));
        });
    }

    [TestCase(0, 0, 1f)]
    [TestCase(100, 0, 1.1f)]
    [TestCase(1000, 0, 2f)]
    [TestCase(2000, 0, 2.5f)]
    [TestCase(3000, 14, 4f)]
    [TestCase(3500, 14, 4.5f)]
    [TestCase(4000, 14, 5f)]
    public void TestCalcCritDamage(int criticalDamage, int mode, float expected) {
        // Act
        float maple2LuaResult = maple2Lua.CalcCritDamage(criticalDamage, mode);
        float luaResult = Server.Game.LuaFunctions.Lua.CalcCritDamage(criticalDamage, mode);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(80, 586, 111, 50, 0, 0, 0.13f)] // random test character
    public void TestCalcPlayerCritRate(int jobCode, long luk, long critRate, long critResistance, int finalCapV, int mode, float expected) {
        // Act
        float maple2LuaResult = maple2Lua.CalcPlayerCritRate(jobCode, luk, critRate, critResistance, finalCapV, mode);
        float luaResult = Server.Game.LuaFunctions.Lua.CalcPlayerCritRate(jobCode, luk, critRate, critResistance, finalCapV, mode);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(100, 50, 0.08f)] // guessed random values
    public void TestCalcNpcCritRate(long critRate, long critResistance, float expected) {
        // Act
        float maple2LuaResult = maple2Lua.CalcNpcCritRate(0, critRate, critResistance);
        float luaResult = Server.Game.LuaFunctions.Lua.CalcNpcCritRate(0, critRate, critResistance);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult, Is.EqualTo(expected).Within(0.01f));
        });
    }

    // The port gives the script's value for every rarity and equip type.
    [Test]
    public void CalcItemMergeRevertMeso() {
        Assert.Multiple(() => {
            for (int rarity = 0; rarity <= 7; rarity++) {
                for (int itemType = 0; itemType <= 99; itemType++) {
                    Assert.That(Server.Game.LuaFunctions.Lua.CalcItemMergeRevertMeso(rarity, itemType),
                        Is.EqualTo(maple2Lua.CalcItemMergeRevertMeso(rarity, itemType)), $"rarity {rarity} type {itemType}");
                }
            }
        });
        Assert.That(Server.Game.LuaFunctions.Lua.CalcItemMergeRevertMeso(4, 22), Is.EqualTo(500000));
    }

    // The outfit dismantle reward: the port gives the script's value for every grade and skin type.
    [Test]
    public void CalcMeratSkinItemBreakReward() {
        int[] skinTypes = [0, 1, 2, 3, 4, 5, 6, 98, 99, 100];
        Assert.Multiple(() => {
            foreach (int skinType in skinTypes) {
                for (int grade = 0; grade <= 7; grade++) {
                    (string, int, int?, int?, string) script = maple2Lua.CalcMeretSkinItemBreakReward(50, grade, 0, skinType);
                    (string, int, int?, int?, string) port = Server.Game.LuaFunctions.Lua.CalcMeratSkinItemBreakReward(50, grade, 0, skinType);
                    Assert.That(port.Item2, Is.EqualTo(script.Item2), $"count, skinType {skinType} grade {grade}");
                    Assert.That(port.Item3, Is.EqualTo(script.Item3), $"item, skinType {skinType} grade {grade}");
                    Assert.That(port.Item4, Is.EqualTo(script.Item4), $"amount, skinType {skinType} grade {grade}");
                    if (script.Item2 > 0) {
                        Assert.That(port.Item1, Is.EqualTo(script.Item1), $"bind, skinType {skinType} grade {grade}");
                        Assert.That(port.Item5, Is.EqualTo(script.Item5), $"tag, skinType {skinType} grade {grade}");
                    }
                }
            }
        });
        Assert.That(Server.Game.LuaFunctions.Lua.CalcMeratSkinItemBreakReward(50, 4, 0, 1).Item4, Is.EqualTo(V<int?>(null, 20)));
        Assert.That(Server.Game.LuaFunctions.Lua.CalcMeratSkinItemBreakReward(50, 2, 0, 3).Item3, Is.EqualTo(V<int?>(null, 40100032)));
    }

    // The outfit socket refund: the percent and an outfit's unlock ingredient match the script.
    [Test]
    public void CalcMeratSkinItemBreakRefundPercent() {
        Assert.That(Server.Game.LuaFunctions.Lua.CalcMeratSkinItemBreakRefundPercent(), Is.EqualTo(maple2Lua.CalcMeretSkinItemBreakRefundPercent()));
        Assert.That(Server.Game.LuaFunctions.Lua.CalcMeratSkinItemBreakRefundPercent(), Is.EqualTo(V(0f, 75f)));
        Assert.Multiple(() => {
            foreach (int skinType in new[] { 1, 2, 3, 4, 5, 99 }) {
                for (int grade = 1; grade <= 6; grade++) {
                    (int, string, int) script = maple2Lua.CalcItemSocketUnlockIngredient(0, grade, 50, 0, skinType);
                    (int, string, int) port = Server.Game.LuaFunctions.Lua.CalcItemSocketUnlockIngredient(0, grade, 50, 0, skinType);
                    Assert.That(port, Is.EqualTo(script), $"skinType {skinType} grade {grade}");
                }
            }
        });
        Assert.That(Server.Game.LuaFunctions.Lua.CalcItemSocketUnlockIngredient(0, 4, 50, 0, 2), Is.EqualTo((1, "SkinCrystal", 690)));
    }

    [TestCase(1, 0)]
    [TestCase(8, 0)]
    [TestCase(10, 100)]
    [TestCase(90, 900)]
    public void TestCalcRevivalPrice(int level, int expected) {
        // Act
        int maple2LuaResult = maple2Lua.CalcRevivalPrice((ushort) level);
        int luaResult = Server.Game.LuaFunctions.Lua.CalcRevivalPrice((ushort) level);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected));
            Assert.That(luaResult, Is.EqualTo(expected));
        });
    }

    [TestCase(5, 10, 137)]
    [TestCase(10, 10, 275)]
    [TestCase(15, 20, 1099)]
    [TestCase(5, 50, 2635)]
    [TestCase(5, 90, 7214)]
    [TestCase(25, 45, 10971)] // floats gave 10972
    [TestCase(32, 63, 25071)] // floats gave 25072
    public void CalcTaxiCharge(int distance, int level, int expected) {
        // Act
        int maple2LuaResult = maple2Lua.CalcTaxiCharge(distance, (ushort) level);
        int luaResult = Server.Game.LuaFunctions.Lua.CalcTaxiCharge(distance, level);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected));
            Assert.That(luaResult, Is.EqualTo(expected));
        });
    }

    [TestCase(10, 30000)]
    [TestCase(50, 50000)]
    [TestCase(90, 70000)]
    public void CalcAirTaxiCharge(int level, int expected) {
        // Act
        int maple2LuaResult = maple2Lua.CalcAirTaxiCharge((ushort) level);
        int luaResult = Server.Game.LuaFunctions.Lua.CalcAirTaxiCharge(level);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected));
            Assert.That(luaResult, Is.EqualTo(expected));
        });
    }

    [TestCase(0, 1, 34, 0, 80, 6, 70, 10)] // fire prism star
    public void TestConstantValueCap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int expected) {
        // Act
        (float, float) maple2LuaResult = maple2Lua.ConstantValueCap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);
        (float, float) luaResult = Server.Game.LuaFunctions.Lua.ConstantValueCap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult.Item1, Is.EqualTo(expected));
            Assert.That(luaResult.Item1, Is.EqualTo(expected));
        });
    }

    [TestCase(0, 1, 34, 0, 80, 6, 70, 67659f, 36222f)] // fire prism star
    public void TestConstantValueWapMax(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, float expectedNa, float expectedKr) {
        float expected = V(expectedNa, expectedKr);
        // Act
        (float, float) maple2LuaResult = maple2Lua.ConstantValueWapMax(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);
        (float, float) luaResult = Server.Game.LuaFunctions.Lua.ConstantValueWapMax(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult.Item1, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult.Item1, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(0, 1, 34, 0, 80, 6, 70, 50008f, 26773f)] // fire prism star
    public void TestConstantValueWapMin(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, float expectedNa, float expectedKr) {
        float expected = V(expectedNa, expectedKr);
        // Act
        (float, float) maple2LuaResult = maple2Lua.ConstantValueWapMin(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);
        (float, float) luaResult = Server.Game.LuaFunctions.Lua.ConstantValueWapMin(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult.Item1, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult.Item1, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(1, 20000)]
    [TestCase(2, 50000)]
    [TestCase(3, 200000)]
    [TestCase(4, 500000)]
    public void TestCalcPetEvolutionCost(int rarity, long expected) {
        Assert.Multiple(() => {
            Assert.That((long) maple2Lua.CalcPetEvolutionCost(rarity), Is.EqualTo(expected));
            Assert.That(Server.Game.LuaFunctions.Lua.CalcPetEvolutionCost(rarity), Is.EqualTo(expected));
        });
    }

    [TestCase(2, 50, 1)]
    [TestCase(5, 70, 1)]
    public void TestCalcPetEvolutionResultLevel(int rarity, int levelLimit, int expected) {
        Assert.Multiple(() => {
            Assert.That((int) maple2Lua.CalcPetEvolutionResultLevel(rarity, (ushort) levelLimit), Is.EqualTo(expected));
            Assert.That(Server.Game.LuaFunctions.Lua.CalcPetEvolutionResultLevel(rarity, levelLimit), Is.EqualTo(expected));
        });
    }

    [TestCase(22000L, 50)]
    [TestCase(22000L, 60)]
    [TestCase(55000L, 70)]
    [TestCase(90000L, 0)]
    public void TestCalcPetExp(long exp, int levelLimit) {
        long ours = Server.Game.LuaFunctions.Lua.CalcPetExp(exp, levelLimit);
        Assert.That(ours, Is.EqualTo((long) maple2Lua.CalcPetExp(exp, (ushort) levelLimit)));
    }

    [TestCase(1, 1, 1, 0L, 0, 10, 50)]
    [TestCase(3, 2, 5, 1234L, 3, 10, 60)]
    [TestCase(4, 3, 10, 0L, 9, 10, 70)]
    [TestCase(4, 4, 1, 500L, 10, 10, 50)]
    [TestCase(5, 5, 30, 77777L, 25, 10, 50)]
    [TestCase(2, 3, 2, 10L, 59, 10, 50)]
    [TestCase(2, 4, 2, 10L, 70, 10, 60)]
    [TestCase(1, 2, 3, 0L, 0, 0, 50)]
    public void TestCalcPetEnchantExp(int targetRarity, int fodderRarity, int fodderLevel, long fodderExp, int count, int limit, int levelLimit) {
        (long Base, long Bonus) ours = Server.Game.LuaFunctions.Lua.CalcPetEnchantExp(targetRarity, fodderRarity, fodderLevel, fodderExp, count, limit, levelLimit);
        var theirs = maple2Lua.CalcPetEnchantExp((ushort) targetRarity, (ushort) fodderRarity, (ushort) fodderLevel, fodderExp, count, limit, (ushort) levelLimit);
        Assert.Multiple(() => {
            Assert.That(ours.Base, Is.EqualTo((long) theirs.Item1));
            Assert.That(ours.Bonus, Is.EqualTo((long) theirs.Item2));
        });
    }

    [TestCase(1, 1, 1, 0L)]
    [TestCase(2, 3, 10, 5000L)]
    [TestCase(3, 1, 25, 0L)]
    [TestCase(4, 2, 2, 120L)]
    [TestCase(5, 5, 50, 0L)]
    public void TestCalcPetEnchantCost(int targetRarity, int fodderRarity, int fodderLevel, long fodderExp) {
        int ours = Server.Game.LuaFunctions.Lua.CalcPetEnchantCost(targetRarity, fodderRarity, fodderLevel, fodderExp);
        Assert.That(ours, Is.EqualTo((int) maple2Lua.CalcPetEnchantCost((ushort) targetRarity, (ushort) fodderRarity, (ushort) fodderLevel, fodderExp)));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void TestCalcPetEnchantBookCost(int rarity) {
        int ours = Server.Game.LuaFunctions.Lua.CalcPetEnchantBookCost(rarity);
        Assert.That(ours, Is.EqualTo((int) maple2Lua.CalcPetEnchantBookCost((ushort) rarity)));
    }

    // The client's own script, in every locale (it has no locale test, unlike the upstream server script): the first
    // five alliances get 2000/1500/1000/800/700 and x2 for a useMainFamePoint of 2 or more, later alliances
    // 230/200/140/100/90 and x10. The rank and the flag are clamped to the tables.
    [TestCase(4, 1, 3, 700)]
    [TestCase(3, 1, 1, 800)]
    [TestCase(0, 2, 5, 4000)]
    [TestCase(1, 3, 2, 3000)]
    [TestCase(4, 1, 6, 90)]
    [TestCase(0, 2, 9, 2300)]
    [TestCase(-1, 0, 1, 2000)]
    [TestCase(5, 1, 1, 0)]
    [TestCase(6, 1, 6, 0)]
    public void TestCalcQuestRewardFamePoint(int rank, int use, int alliance, int expected) {
        Assert.That(Server.Game.LuaFunctions.Lua.CalcQuestRewardFamePoint(rank, 0, use, alliance), Is.EqualTo(expected));
    }

    // The client's calcGatheringObjectgSuccessProp at KR recipe limits: 30000001 (high 8, normal 10, max count 13) and
    // 30000003 (14, 17, max 22). The upstream server script uses 0.3 where the client uses 0.35, truncates the 0.2 home
    // scaling and has no max-count case; the expected values are the client's.
    [TestCase(7, 8, 10, 1, 100f)]
    [TestCase(10, 8, 10, 1, 84.508f)]
    [TestCase(12, 8, 10, 1, 38.032f)]
    [TestCase(13, 8, 10, 1, 0f)]
    [TestCase(20, 14, 17, 1, 57.123f)]
    [TestCase(22, 14, 17, 1, 0f)]
    [TestCase(3, 14, 17, 0, 97.368f)] // another player's home: limits 2.8 and 3.4
    public void TestCalcGatheringObjectSuccessRate_ClientScript(int count, int high, int normal, int home, float expected) {
        Assert.That(Server.Game.LuaFunctions.Lua.CalcGatheringObjectSuccessRate(count, high, normal, home), Is.EqualTo(expected).Within(0.005f));
    }

    [TestCase(8, 10, 13)]
    [TestCase(14, 17, 22)]
    public void TestCalcGatheringObjectMaxCount_ClientScript(int high, int normal, int expected) {
        Assert.That(Server.Game.LuaFunctions.Lua.CalcGatheringObjectMaxCount(high, normal), Is.EqualTo(expected));
    }

    // The client's calcSendMailFee in doubles, truncated to a 64-bit integer by CLua::CalcSendMailFee.
    [TestCase(0, 0, 0, 0, 0, 0, 0, 0L)]
    [TestCase(50, 1, 0, 0, 0, 0, 0, 6L)]
    [TestCase(1000, 1, 1, 0, 0, 0, 1, 53L)]
    [TestCase(999, 0, 0, 0, 0, 0, 0, 49L)]
    [TestCase(16777219, 0, 0, 0, 0, 0, 0, 838860L)] // floats gave 838861
    [TestCase(1024846460, 0, 0, 0, 0, 0, 0, 51242323L)] // floats gave 51242324
    public void TestCalcSendMailFee_ClientScript(int meso, int normal, int rare, int elite, int excellent, int legendary, int epic, long expected) {
        Assert.That(Server.Game.LuaFunctions.Lua.CalcSendMailFee(meso, normal, rare, elite, excellent, legendary, epic), Is.EqualTo(expected));
    }
}
