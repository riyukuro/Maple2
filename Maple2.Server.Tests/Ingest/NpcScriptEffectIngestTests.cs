using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// npcScriptFunction_Final.xml's skillID / skillLevel, which the parser does not read, keyed by npc, script and
// function. The first row is KR's only one with a skillID (the Altar of Rage); a row without one is left out, and a
// row without a skillLevel is level 1.
public class NpcScriptEffectIngestTests {
    [Test]
    public void TheEffectRows_AreKeyedByNpcScriptAndFunction() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <function npcID="11000674" scriptID="11" functionID="1" skillID="70000030" skillLevel="1" />
              <function npcID="11001210" scriptID="41" functionID="1" collectItemID="30000419" collectItemAmount="1" />
              <function npcID="1000" scriptID="2" functionID="3" skillID="70000031" />
            </ms2>
            """);

        Dictionary<(int NpcId, int ScriptId, int FunctionId), (int Id, short Level)> effects = ServerTableMapper.NpcScriptEffects(document);

        Assert.That(effects, Is.EqualTo(new Dictionary<(int, int, int), (int, short)> {
            [(11000674, 11, 1)] = (70000030, 1),
            [(1000, 2, 3)] = (70000031, 1),
        }));
    }
}
