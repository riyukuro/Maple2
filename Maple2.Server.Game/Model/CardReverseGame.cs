using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Tools;

namespace Maple2.Server.Game.Model;

// The card game of a Ui object (UICardReverseGameDialog, uicardreversegamedialog.cpp): its deck is a BonusGame row
// (BonusGame.xml gameType 5, the cost; BonusGameDrop.xml, the cards and their weights). A shuffle takes the cost and
// gives three picks (the client sets its pick count to 3 on the shuffle answer, :226); each pick opens one card of the
// deck, by its weight.
// Chosen here: a card opened is not opened again before the next shuffle (the client takes the opened card out of the
// cards it shows face down, :241-248, so the same card twice would show one card for two picks).
public sealed class CardReverseGame {
    public const string Swf = "CardReverseGameDialog";
    public const int PicksPerShuffle = 3;
    public const int GameType = 5;

    public readonly BonusGameTable.Game Game;
    public readonly BonusGameTable.Drop Drop;
    public int Picks { get; private set; }
    private readonly HashSet<int> opened = [];
    // The field of the object that opened the page: the page's messages from any other field are dropped.
    public FieldManager? Field { get; init; }

    public CardReverseGame(BonusGameTable.Game game, BonusGameTable.Drop drop) {
        Game = game;
        Drop = drop;
    }

    // The game of a Ui object that opens the card game page, if its value names a card game row (gameType 5) with cards.
    // The deck holds only the cards whose item the client can read (readable): the page's deck reader makes each card's
    // item and, when the client has no item data for its id, still asks the item's name, which reads the missing data
    // and crashes the client (uicardreversegamedialog.cpp:202-216; CItem name, commonfiles/item.cpp:2271).
    public static CardReverseGame? Of(InteractObjectMetadataUi? ui, BonusGameTable table, FieldManager? field = null, Func<int, bool>? readable = null) {
        if (ui?.Swf != Swf || !int.TryParse(ui.Value, out int gameId)) {
            return null;
        }
        if (!table.Games.TryGetValue(gameId, out BonusGameTable.Game? game) || !table.Drops.TryGetValue(gameId, out BonusGameTable.Drop? drop) ||
            game.Type != GameType) {
            return null;
        }
        if (readable != null) {
            drop = drop with { Items = drop.Items.Where(card => readable(card.ItemComponent.ItemId)).ToArray() };
        }
        if (drop.Items.Length == 0) {
            return null;
        }
        return new CardReverseGame(game, drop) { Field = field };
    }

    // The client has item data for the id: the server knows the item and the played client's pack has it
    // (ItemMetadataProperty.NotInPlayedClient, set when the ingest is given the played client's pack).
    public static bool Readable(ItemMetadataStorage items, int itemId) {
        return items.TryGet(itemId, out ItemMetadata? item) && !item.Property.NotInPlayedClient;
    }

    // The deck sent again (the page opened anew): no picks left until the next shuffle.
    public void Reset() {
        Picks = 0;
        opened.Clear();
    }

    // The cost was taken: three picks, every card face down again.
    public void Shuffle() {
        Picks = PicksPerShuffle;
        opened.Clear();
    }

    // The deck index of the card opened, by weight among the cards not opened since the shuffle; -1 with no pick left.
    public int Pick(Random? random = null) {
        int index = Draw(random);
        if (index >= 0) {
            Open(index);
        }
        return index;
    }

    // The card a pick would open, the pick not yet used up (Open uses it once the card's item is made).
    public int Draw(Random? random = null) {
        if (Picks <= 0) {
            return -1;
        }
        var cards = new WeightedSet<int>(random);
        for (int i = 0; i < Drop.Items.Length; i++) {
            if (!opened.Contains(i)) {
                cards.Add(i, Drop.Items[i].Probability);
            }
        }
        if (cards.Count == 0) {
            return -1;
        }
        return cards.Get();
    }

    public void Open(int index) {
        if (Picks > 0 && opened.Add(index)) {
            Picks--;
        }
    }
}
