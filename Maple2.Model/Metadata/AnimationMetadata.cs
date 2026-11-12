namespace Maple2.Model.Metadata;

public record AnimationMetadata(string Model, IReadOnlyDictionary<string, AnimationSequenceMetadata> Sequences) {
    // The sequences keyed without regard to case: trigger scripts name a model's sequences in another case at times
    // (set_npc_emotion_sequence "ChatUP_A" for a model whose sequence is "ChatUp_A"); the found sequence keeps the
    // model's own name. On a clash in case only, the first one is kept.
    public static IReadOnlyDictionary<string, AnimationSequenceMetadata> IgnoreCase(IReadOnlyDictionary<string, AnimationSequenceMetadata> sequences) {
        var result = new Dictionary<string, AnimationSequenceMetadata>(sequences.Count, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, AnimationSequenceMetadata sequence) in sequences) {
            result.TryAdd(name, sequence);
        }
        return result;
    }
}

public record AnimationSequenceMetadata(string Name, short Id, float Time, List<AnimationKey> Keys);

public record AnimationKey(string Name, float Time);
