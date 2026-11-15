using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record ConditionMetadata(
    ConditionType Type,
    long Value,
    ConditionMetadata.Parameters? Codes,
    ConditionMetadata.Parameters? Target,
    int PartyCount = 0,
    int GuildPartyCount = 0) {

    public record Parameters(
        string[]? Strings = null,
        Range<int>? Range = null,
        int[]? Integers = null,
        // The ranges of a list that mixes "a-b" ranges with ids or other ranges ("02010001-02019999,02000335"):
        // CConditionParam::Parse (conditionutil.cpp) reads each comma item, and any range or id may match.
        Range<int>[]? Ranges = null);

    public readonly record struct Range<T>(T Min, T Max) where T : INumber<T>;
}
