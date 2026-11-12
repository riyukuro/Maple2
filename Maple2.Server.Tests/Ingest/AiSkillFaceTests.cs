using System.Collections.Generic;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Parser.Xml.AI;

namespace Maple2.Server.Tests.Ingest;

// An AI skill node without faceTarget turns the npc to its target at the cast; one that writes faceTarget="0" does
// not. The parser reads both as 0, so the ingest tells them apart from the file.
public class AiSkillFaceTests {
    private const string Xml = """
        <npcAi>
            <reserved>
                <condition name="hpLess" value="50">
                    <node name="skill" idx="8" />
                </condition>
            </reserved>
            <battle>
                <!-- <node name="skill" idx="9" faceTarget="1" /> -->
                <node name="select" prob="50,50">
                    <node name="skill" faceTarget="0" idx="5">
                        <node name="standby" limit="2000" faceTarget="0" />
                        <node name="Skill" idx="1" />
                    </node>
                    <node name="standby" limit="100" faceTarget="1">
                        <node name="skill" idx="2" faceTarget="1" />
                    </node>
                </node>
            </battle>
            <aiPresets>
                <aiPreset name="Main">
                    <node name="skill" idx="3" />
                </aiPreset>
            </aiPresets>
        </npcAi>
        """;

    [Test]
    public void WrittenFollowsTheFileOrder() {
        Assert.That(AiSkillFace.Written(Xml), Is.EqualTo(new[] { false, true, false, true, false }));
    }

    private static T With<T>(T entry, params Entry[] children) where T : Entry {
        entry.Entries = new List<Entry>(children);
        return entry;
    }

    private static (NpcAi Data, SkillNode[] Skills) Parsed() {
        var skills = new SkillNode[5];
        for (int i = 0; i < skills.Length; i++) {
            skills[i] = new SkillNode { name = "skill" };
        }
        skills[3].faceTarget = 1;
        var data = new NpcAi();
        data.Reserved.Add(With(new HpLessCondition { name = "hpLess" }, skills[0]));
        data.Battle.Add(With(new SelectNode { name = "select" },
            With(skills[1], new StandbyNode { name = "standby" }, skills[2]),
            With(new StandbyNode { name = "standby", faceTarget = 1 }, skills[3])));
        data.AiPresets.Add(With(new AiPresetEntry { name = "Main" }, skills[4]));
        return (data, skills);
    }

    [Test]
    public void NodesWithoutTheAttributeAreFound() {
        (NpcAi data, SkillNode[] skills) = Parsed();
        HashSet<SkillNode>? unwritten = AiSkillFace.UnwrittenNodes(data, Xml);
        Assert.That(unwritten, Is.Not.Null);
        Assert.That(unwritten, Is.EquivalentTo(new[] { skills[0], skills[2], skills[4] }));
        Assert.That(AiSkillFace.Unwritten, Is.EqualTo(1));
    }

    // A file whose skill nodes do not line up with the parsed ones changes nothing.
    [Test]
    public void DifferentCountsGiveNoAnswer() {
        (NpcAi data, _) = Parsed();
        data.Battle.Add(new SkillNode { name = "skill" });
        Assert.That(AiSkillFace.UnwrittenNodes(data, Xml), Is.Null);
    }
}
