using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record BonusGameTable(
    IReadOnlyDictionary<int, BonusGameTable.Game> Games,
    IReadOnlyDictionary<int, BonusGameTable.Drop> Drops) : ServerTable {


    // Type: the row's gameType (1 and 4 the roulettes, 5 the card game of CardReverseGameDialog).
    public record Game(
        int Id,
        int Type,
        ItemComponent ConsumeItem,
        Game.Slot[] Slots) {

        public record Slot(
            int MinProp,
            int MaxProp);
    }

    public record Drop(
        int Id,
        BonusGameTable.Drop.Item[] Items) {

        public record Item(
            ItemComponent ItemComponent,
            int Probability,
            bool Notice);
    }
}
