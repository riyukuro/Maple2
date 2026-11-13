using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record FishTable(
    IReadOnlyDictionary<int, FishTable.Fish> Fishes,
    IReadOnlyDictionary<int, FishTable.Spot> Spots,
    IReadOnlyDictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> Lures, // BuffId, BuffLevel
    IReadOnlyDictionary<int, FishTable.FishBox> GlobalFishBoxes,
    IReadOnlyDictionary<int, FishTable.FishBox> IndividualFishBoxes) : ServerTable {

    public record Fish(
        int Id,
        LiquidType FluidHabitat,
        int Mastery,
        int Level,
        short Rarity,
        int PointCount,
        int MasteryExp,
        int Exp,
        int FishingTime,
        int CatchProbability,
        int BaitProbability,
        Range<int> SmallSize,
        Range<int> BigSize,
        int[] BaitEffectIds,
        int IndividualDropBoxId,
        bool IgnoreSpotMastery);

    public readonly record struct Range<T>(T Min, T Max) where T : INumber<T>;

    public record Spot(
        int Id,
        int MinMastery,
        int MaxMastery,
        IReadOnlyList<LiquidType> LiquidTypes,
        int GlobalFishBoxId,
        int IndividualFishBoxId,
        int GlobalDropBoxId,
        int IndividualDropBoxId,
        int SpotLevel,
        int DropRank);

    // A lure is a buff (additionalEffectCode at additionalEffectLevel) held while casting.
    public record Lure(
        int BuffId,
        short BuffLevel,
        Lure.RankRate[] Catches, // added to the bite chance (out of 10000) of fish of the rank
        Lure.RankRate[] Spawns, // scales the box weight of fish of the rank (10000 = unchanged)
        Lure.Spawn[] SpawnFishes, // fish the lure draws out of the boxes, out of 1000
        int FishSizeClass,
        int FishSizeProbability, // out of 10000
        Lure.Drop? GlobalDrop,
        Lure.Drop? IndividualDrop) {
        // Rank 0 counts for every rank (the table's catchProp without a catchRank).
        public record RankRate(
            int Rank,
            int Rate);

        public record Spawn(
            int FishId,
            int Rate);

        // A null mastery range counts at every spot.
        public record Drop(
            int BoxId,
            int Rank,
            Range<int>? SpotMastery);
    }

    public record FishBox(
        int Id,
        int Probability,
        int CubeRate,
        IReadOnlyDictionary<int, int> Fishes) { // FishId, Weight
    }


}
