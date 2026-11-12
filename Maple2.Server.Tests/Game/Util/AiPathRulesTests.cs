using System;
using System.Collections.Generic;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The AI file an npc row names, resolved against the AI files the server data ships.
public class AiPathRulesTests {
    private static readonly HashSet<string> Shipped = ["AI_Default.xml", "AI_Default_Summon.xml", "AI_GigantikaHead_Summon_1.xml",
        "BossDungeon/SnowKing/AI_SnowKing.xml", "BossDungeon/SnowKing_Normal/AI_SnowKing_Chaos.xml",
        "BossDungeon/Pinkbean/AI_StatueHuman01.xml", "BossDungeon/Pinkbean_Normal/Chaos/AI_StatueHuman01.xml"];

    private static string? Resolve(string path) => AiPathRules.Resolve(path, Shipped.Contains);

    [Test]
    public void SummonIdName_UsesTheTemplate() {
        // npc 21420503: "AI_Default_Summon_24003227.xml", summonNpcIDs="24003227".
        Assert.That(Resolve("AI_Default_Summon_24003227.xml"), Is.EqualTo("AI_Default_Summon.xml"));
    }

    [Test]
    public void ShippedName_IsKept() {
        Assert.That(Resolve("AI_GigantikaHead_Summon_1.xml"), Is.EqualTo("AI_GigantikaHead_Summon_1.xml"));
        Assert.That(Resolve("AI_Default.xml"), Is.EqualTo("AI_Default.xml"));
    }

    [Test]
    public void MissingWithoutATemplate_IsNull() {
        Assert.That(Resolve("Test/AI_ZakumBrown_Test.xml"), Is.Null);
        Assert.That(Resolve("AI_Nothing_12.xml"), Is.Null);
    }

    [Test]
    public void MissingBossFile_UsesTheNormalFolder() {
        Assert.Multiple(() => {
            // npc 23000114, room 23021003.
            Assert.That(Resolve("BossDungeon/SnowKing/AI_SnowKing_Chaos.xml"), Is.EqualTo("BossDungeon/SnowKing_Normal/AI_SnowKing_Chaos.xml"));
            // npc 21500835: the same path under the _Normal folder, not the file of that name in BossDungeon/Pinkbean/.
            Assert.That(Resolve("BossDungeon/Pinkbean/Chaos/AI_StatueHuman01.xml"), Is.EqualTo("BossDungeon/Pinkbean_Normal/Chaos/AI_StatueHuman01.xml"));
            Assert.That(Resolve("BossDungeon/SnowKing/AI_SnowKing.xml"), Is.EqualTo("BossDungeon/SnowKing/AI_SnowKing.xml"));
            Assert.That(Resolve("BossDungeon/SnowKing/AI_Nothing.xml"), Is.Null);
            Assert.That(Resolve("Test/AI_SnowKing_Chaos.xml"), Is.Null);
        });
    }

    [TestCase("BossDungeon/SnowKing/AI_SnowKing_Chaos.xml", "BossDungeon/SnowKing_Normal/AI_SnowKing_Chaos.xml")]
    [TestCase("BossDungeon/SnowKing_Normal/AI_X.xml", null)]
    [TestCase("BossDungeon/AI_X.xml", null)]
    [TestCase("Field/SnowKing/AI_X.xml", null)]
    public void NormalFolder(string path, string? expected) {
        Assert.That(AiPathRules.NormalFolder(path), Is.EqualTo(expected));
    }
}
