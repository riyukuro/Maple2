using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record ItemMergeTable(
    IReadOnlyDictionary<int, Dictionary<int, ItemMergeTable.Entry>> Entries) : ServerTable {
    public record Entry(
        int Slot,
        long MesoCost,
        ItemComponent[] Materials,
        IReadOnlyDictionary<BasicAttribute, Option> BasicOptions,
        IReadOnlyDictionary<SpecialAttribute, Option> SpecialOptions
    );

    public record Option(
        Range<int>[] Values,
        Range<int>[] Rates,
        int[] Weights) {
        // The table keeps a step in Values or in Rates by the kind of the attribute and leaves the other
        // one 0. The client has one int range per step whatever the kind (CItemMergeOptionData::Load,
        // itemmergeoptiontable.cpp:333), a rate in thousandths.
        public Range<int> Step(int index) {
            return Values[index].Max != 0 ? Values[index] : Rates[index];
        }

        // First step's minimum to last step's maximum: the range the client shows for the option.
        public Range<int> Bounds() {
            return Values.Length == 0 ? new Range<int>(0, 0) : new Range<int>(Step(0).Min, Step(Values.Length - 1).Max);
        }
    }

    public readonly record struct Range<T>(T Min, T Max) where T : INumber<T>;
}

