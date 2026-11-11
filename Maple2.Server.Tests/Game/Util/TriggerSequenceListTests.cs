using System.Collections.Generic;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// set_npc_emotion_sequence names a comma list on 28 placed actions; the client plays a list one name at a time.
public class TriggerSequenceListTests {
    private static readonly Dictionary<string, string> Rig = new() {
        ["Attack_01_C"] = "Attack_01_C",
        ["Attack_Idle_A"] = "Attack_Idle_A",
        ["Event_02_A"] = "Event_02_A",
        ["Down_Idle_A"] = "Down_Idle_A",
    };

    private static string? Lookup(string name) => Rig.GetValueOrDefault(name);

    [Test]
    public void OneNameIsAListOfOne() {
        Assert.That(TriggerSequenceList.Split("Attack_01_A"), Is.EqualTo(new[] { "Attack_01_A" }));
    }

    [Test]
    public void CommaListSplitsInOrder() {
        Assert.That(TriggerSequenceList.Split("Attack_01_C,Attack_Idle_A"), Is.EqualTo(new[] { "Attack_01_C", "Attack_Idle_A" }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" , ,")]
    public void BlankListIsEmpty(string? value) {
        Assert.That(TriggerSequenceList.Split(value), Is.Empty);
    }

    [Test]
    public void SpacesAndEmptyEntriesAreDropped() {
        Assert.That(TriggerSequenceList.Split(" Attack_01_C ,,Attack_Idle_A,"), Is.EqualTo(new[] { "Attack_01_C", "Attack_Idle_A" }));
    }

    // 52000137 plays Event_02_A and then Down_Idle_A four times: a repeated name stays in the list.
    [Test]
    public void RepeatedNamePlaysAgain() {
        var missing = new List<string>();
        string[] names = TriggerSequenceList.Playable(
            TriggerSequenceList.Split("Event_02_A,Down_Idle_A,Down_Idle_A,Down_Idle_A,Down_Idle_A"), Lookup, missing);
        Assert.Multiple(() => {
            Assert.That(names, Is.EqualTo(new[] { "Event_02_A", "Down_Idle_A", "Down_Idle_A", "Down_Idle_A", "Down_Idle_A" }));
            Assert.That(missing, Is.Empty);
        });
    }

    [Test]
    public void NameTheRigLacksIsSkipped() {
        var missing = new List<string>();
        string[] names = TriggerSequenceList.Playable(TriggerSequenceList.Split("IceSphere_A,Attack_Idle_A"), Lookup, missing);
        Assert.Multiple(() => {
            Assert.That(names, Is.EqualTo(new[] { "Attack_Idle_A" }));
            Assert.That(missing, Is.EqualTo(new[] { "IceSphere_A" }));
        });
    }

    // Before, the whole list was one lookup, which no rig has.
    [Test]
    public void WholeListIsNoName() {
        Assert.That(Lookup("Attack_01_C,Attack_Idle_A"), Is.Null);
    }
}
