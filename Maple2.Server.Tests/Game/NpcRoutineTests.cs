using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// The routines an npc picks between standing: an action its model has no sequence for keeps its weight as standby.
public class NpcRoutineTests {
    private static Dictionary<string, AnimationSequenceMetadata> Model(params string[] names) =>
        names.ToDictionary(name => name, name => new AnimationSequenceMetadata(name, 1, 1f, []));

    private static Dictionary<string, int> Picks(IEnumerable<NpcAction> actions, IReadOnlyDictionary<string, AnimationSequenceMetadata> model, int count) {
        var routines = FieldNpc.Routines(actions, model, new Random(1));
        var picks = new Dictionary<string, int>();
        for (int i = 0; i < count; i++) {
            string key = routines.Get() ?? "standby";
            picks[key] = picks.GetValueOrDefault(key) + 1;
        }
        return picks;
    }

    // Walk_A 30 + Bore_A 70 on a model without Bore_A: about 70% standby, the walk keeps its 30%.
    [Test]
    public void MissingAction_IsStandbyWithItsWeight() {
        Dictionary<string, int> picks = Picks([new NpcAction("Walk_A", 30), new NpcAction("Bore_A", 70)], Model("Idle_A", "Walk_A"), 10000);
        Assert.Multiple(() => {
            Assert.That(picks.Keys, Is.EquivalentTo(new[] { "standby", "Walk_A" }));
            Assert.That(picks["standby"], Is.InRange(6700, 7300));
            Assert.That(picks["Walk_A"], Is.InRange(2700, 3300));
        });
    }

    [Test]
    public void AllPresent_KeepTheirWeights() {
        Dictionary<string, int> picks = Picks([new NpcAction("Walk_A", 30), new NpcAction("Bore_A", 70)], Model("Walk_A", "Bore_A"), 10000);
        Assert.Multiple(() => {
            Assert.That(picks.Keys, Is.EquivalentTo(new[] { "Bore_A", "Walk_A" }));
            Assert.That(picks["Bore_A"], Is.InRange(6700, 7300));
        });
    }

    // Every weight 0: the first action, as before; standby when the model lacks it.
    [Test]
    public void AllWeightZero_FirstAction() {
        NpcAction[] actions = [new NpcAction("Bore_A", 0), new NpcAction("Walk_A", 0)];
        Assert.Multiple(() => {
            Assert.That(Picks(actions, Model("Walk_A", "Bore_A"), 100), Is.EqualTo(new Dictionary<string, int> { ["Bore_A"] = 100 }));
            Assert.That(Picks(actions, Model("Walk_A"), 100), Is.EqualTo(new Dictionary<string, int> { ["standby"] = 100 }));
        });
    }

    [Test]
    public void NoActions_Empty() {
        Assert.That(FieldNpc.Routines([], Model("Idle_A")).Count, Is.EqualTo(0));
    }
}
