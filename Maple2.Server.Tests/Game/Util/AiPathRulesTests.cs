using System;
using System.Collections.Generic;
﻿using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The AI file an npc row names, resolved against the AI files the server data ships.
public class AiPathRulesTests {
    private static readonly HashSet<string> Shipped = ["AI_Default.xml", "AI_Default_Summon.xml", "AI_GigantikaHead_Summon_1.xml"];

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
}
