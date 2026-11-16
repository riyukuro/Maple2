using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// The parser reads no attribute of <battle>; the ingest takes startAni and endAni from the file (KR: 70 files).
public class AiBattleAnimationTests {
    [Test]
    public void ReadsBothFromTheBattleTag() {
        const string xml = """
            <npcAi>
                <!-- <battle startAni="Old_A"> -->
                <battle startAni="Up_A" endAni="Down_A">
                    <node name="standby" limit="100" />
                </battle>
            </npcAi>
            """;
        Assert.That(AiMapper.BattleAnimations(xml), Is.EqualTo(("Up_A", "Down_A")));
    }

    [Test]
    public void APlainBattleHasNone() {
        Assert.That(AiMapper.BattleAnimations("<npcAi><battle><node name=\"standby\" /></battle><battleEnd /></npcAi>"), Is.EqualTo(("", "")));
    }
}
