using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The card game page (UICardReverseGameDialog): one byte, 0 when the page opens (uicardreversegamedialog.cpp:162),
// 1 for the shuffle button (:281), 2 for a card click (:293; the card clicked stays in the client). The game is the one
// of the Ui object the player used (InteractObjectHandler).
public class CardReverseGameHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.CardReverseGame;

    private enum Command : byte {
        Load = 0,
        Shuffle = 1,
        Pick = 2,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        CardReverseGame? game = session.CardReverseGame;
        if (game == null) {
            return;
        }

        var command = packet.Read<Command>();
        switch (command) {
            case Command.Load:
                game.Reset();
                session.Send(CardReverseGamePacket.Deck(game.Game, game.Drop));
                return;
            case Command.Shuffle:
                HandleShuffle(session, game);
                return;
            case Command.Pick:
                HandlePick(session, game);
                return;
        }
    }

    private static void HandleShuffle(GameSession session, CardReverseGame game) {
        if (!session.Item.Inventory.ConsumeItemComponents([game.Game.ConsumeItem])) {
            session.Send(CardReverseGamePacket.LackCost());
            return;
        }
        game.Shuffle();
        session.Send(CardReverseGamePacket.Shuffle());
    }

    private void HandlePick(GameSession session, CardReverseGame game) {
        if (session.Field == null) {
            return;
        }
        int index = game.Pick();
        if (index < 0) {
            return;
        }

        BonusGameTable.Drop.Item card = game.Drop.Items[index];
        session.Send(CardReverseGamePacket.Pick(index));
        Item? item = session.Field.ItemDrop.CreateItem(card.ItemComponent.ItemId, card.ItemComponent.Rarity, card.ItemComponent.Amount);
        if (item == null) {
            Logger.Warning("CardReverseGame: no item {ItemId} for card {Index} of game {GameId}", card.ItemComponent.ItemId, index, game.Game.Id);
            return;
        }
        if (!session.Item.Inventory.Add(item, true)) {
            session.Item.MailItem(item);
        }
        BonusGameHandler.SendDropNotices(session, [(card, item)]);
    }
}
