using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Game.Util;

// The AI parser reads no attribute of <battleEnd>; the mapper reads onlyDead from the file.
public class AiBattleEndTests {
    // AI_DefaultNew_ShadowGate.xml: the random room portal opens only when the npc dies.
    [Test]
    public void OnlyDead() {
        const string xml = "<npcAi><battle /><battleEnd onlyDead=\"true\"><node name=\"CreateRandomRoom\" randomRoomID=\"3\" portalDuration=\"30\" /></battleEnd></npcAi>";
        Assert.That(AiMapper.BattleEndOnlyDead(xml), Is.True);
    }

    [TestCase("<battleEnd onlyDead=\"true\" isKeepBattle=\"true\">")]
    [TestCase("<battleEnd isKeepBattle=\"true\" onlyDead=\"true\">")]
    [TestCase("<battleEnd\r\n  onlyDead = \"TRUE\" >")]
    [TestCase("<battleEnd onlyDead=\"1\"/>")]
    public void OnlyDeadForms(string xml) {
        Assert.That(AiMapper.BattleEndOnlyDead(xml), Is.True);
    }

    // A plain battleEnd runs at a live battle end too.
    [TestCase("<battleEnd><node name=\"SetValue\" key=\"a\" value=\"1\" /></battleEnd>")]
    [TestCase("<battleEnd onlyDead=\"false\">")]
    [TestCase("<battle onlyDead=\"true\"></battle><battleEnd>")]
    [TestCase("<!-- no battleEnd --><battle />")]
    // AI_MafiaPunch.xml: a plain tree whose comment explains onlyDead.
    [TestCase("<battleEnd>\n<!--at death; write <battleEnd onlyDead=\"true\"> to run it only at death-->\n<node name=\"conditions\" /></battleEnd>")]
    public void NotOnlyDead(string xml) {
        Assert.That(AiMapper.BattleEndOnlyDead(xml), Is.False);
    }
}
