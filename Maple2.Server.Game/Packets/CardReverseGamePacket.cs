using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The card game page's server data (0xDA, UICardReverseGameDialog::vfunc_79, uicardreversegamedialog.cpp:176).
public static class CardReverseGamePacket {
    private enum Command : byte {
        Deck = 0,
        Shuffle = 1,
        Pick = 2,
        Result = 3,
    }

    // The cost item and count, then each card: item id, rank (byte), count (:187-196).
    public static ByteWriter Deck(BonusGameTable.Game game, BonusGameTable.Drop drop) {
        var pWriter = Packet.Of(SendOp.CardReverseGame);
        pWriter.Write<Command>(Command.Deck);
        pWriter.WriteInt(game.ConsumeItem.ItemId);
        pWriter.WriteInt(game.ConsumeItem.Amount);
        pWriter.WriteInt(drop.Items.Length);
        foreach (BonusGameTable.Drop.Item item in drop.Items) {
            pWriter.WriteInt(item.ItemComponent.ItemId);
            pWriter.WriteByte((byte) item.ItemComponent.Rarity);
            pWriter.WriteInt(item.ItemComponent.Amount);
        }
        return pWriter;
    }

    // The cards are shuffled: three picks (:224-233).
    public static ByteWriter Shuffle() {
        var pWriter = Packet.Of(SendOp.CardReverseGame);
        pWriter.Write<Command>(Command.Shuffle);
        return pWriter;
    }

    // The deck index of the card the pick opened (:236-251).
    public static ByteWriter Pick(int index) {
        var pWriter = Packet.Of(SendOp.CardReverseGame);
        pWriter.Write<Command>(Command.Pick);
        pWriter.WriteInt(index);
        return pWriter;
    }

    // Result 0: too few of the cost item, the box naming it (string 0xc3f), the shuffle button on again (:252-262).
    public static ByteWriter LackCost() {
        var pWriter = Packet.Of(SendOp.CardReverseGame);
        pWriter.Write<Command>(Command.Result);
        pWriter.WriteByte(0);
        return pWriter;
    }
}
