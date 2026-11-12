using System.Collections.Generic;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

public class AnimationSequenceCaseTests {
    private static AnimationSequenceMetadata Sequence(string name, short id) => new(name, id, 1f, []);

    [Test]
    public void TriggerNameInAnotherCaseFindsTheModelsSequence() {
        var sequences = AnimationMetadata.IgnoreCase(new Dictionary<string, AnimationSequenceMetadata> {
            ["Idle_A"] = Sequence("Idle_A", 1),
            ["ChatUp_A"] = Sequence("ChatUp_A", 2),
        });

        Assert.That(sequences.TryGetValue("ChatUP_A", out AnimationSequenceMetadata? found), Is.True);
        Assert.That(found!.Name, Is.EqualTo("ChatUp_A"));
        Assert.That(sequences["Idle_A"].Id, Is.EqualTo(1));
        Assert.That(sequences.ContainsKey("Walk_A"), Is.False);
    }

    [Test]
    public void ClashInCaseOnlyKeepsTheFirst() {
        var sequences = AnimationMetadata.IgnoreCase(new Dictionary<string, AnimationSequenceMetadata> {
            ["Bore_A"] = Sequence("Bore_A", 1),
            ["BORE_A"] = Sequence("BORE_A", 2),
        });

        Assert.That(sequences, Has.Count.EqualTo(1));
        Assert.That(sequences["bore_a"].Id, Is.EqualTo(1));
    }
}
