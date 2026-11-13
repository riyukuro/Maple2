using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class MapleopolyHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Mapleopoly;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        Load = 0,
        Roll = 1,
        Result = 3,
        DebugDice = 5,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Roll:
                HandleRoll(session);
                return;
            case Command.Result:
                HandleResult(session);
                return;
            case Command.DebugDice:
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                // The debug dice button: one roll with the two dice the GM picked. It costs what a roll costs.
                int debugDice1 = packet.ReadInt();
                int debugDice2 = packet.ReadInt();
                if (!DebugButtonRules.DiceAllowed(debugDice1, debugDice2)) {
                    return;
                }
                HandleRoll(session, debugDice1, debugDice2);
                return;
        }
    }

    private void HandleLoad(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BlueMarble).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BlueMarble blueMarble) {
            return;
        }

        int freeRollValue = session.GameEvent.Get(GameEventUserValueType.MapleopolyFreeRollAmount, gameEvent.Id, gameEvent.EndTime).Int();
        int totalSlotValue = session.GameEvent.Get(GameEventUserValueType.MapleopolyTotalSlotCount, gameEvent.Id, gameEvent.EndTime).Int();

        // The client shows the fourth value as the ticket count one roll costs; it counts the owned tickets itself.
        session.Send(MapleopolyPacket.Load(blueMarble.Slots, blueMarble.RequiredItem.ItemId, totalSlotValue, freeRollValue, blueMarble.RequiredItem.Amount));
    }

    private void HandleRoll(GameSession session, int dice1 = 0, int dice2 = 0) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BlueMarble).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BlueMarble blueMarble) {
            return;
        }

        int freeRollValue = session.GameEvent.Get(GameEventUserValueType.MapleopolyFreeRollAmount, gameEvent.Id, gameEvent.EndTime).Int();
        int totalSlotValue = session.GameEvent.Get(GameEventUserValueType.MapleopolyTotalSlotCount, gameEvent.Id, gameEvent.EndTime).Int();

        if (freeRollValue > 0) {
            freeRollValue--;
        } else {
            if (!session.Item.Inventory.ConsumeItemComponents(new List<ItemComponent> {
                    blueMarble.RequiredItem,
                })) {
                session.Send(MapleopolyPacket.Error(MapleopolyError.s_bluemarble_result_consume_fail));
                return;
            }
        }

        // roll two dice, unless the GM debug button gave them
        if (!DebugButtonRules.DiceAllowed(dice1, dice2)) {
            dice1 = Random.Shared.Next(1, 7);
            dice2 = Random.Shared.Next(1, 7);
        }

        (totalSlotValue, freeRollValue) = DebugButtonRules.AfterRoll(totalSlotValue, freeRollValue, dice1, dice2);

        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.MapleopolyFreeRollAmount, freeRollValue);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.MapleopolyTotalSlotCount, totalSlotValue);
        session.GameEvent.Mapleopoly.Rolled();
        session.Send(MapleopolyPacket.Roll(totalSlotValue, dice1, dice2, freeRollValue));
    }

    private void HandleResult(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BlueMarble).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BlueMarble blueMarble) {
            return;
        }

        int freeRollValue = session.GameEvent.Get(GameEventUserValueType.MapleopolyFreeRollAmount, gameEvent.Id, gameEvent.EndTime).Int();
        int totalSlotValue = session.GameEvent.Get(GameEventUserValueType.MapleopolyTotalSlotCount, gameEvent.Id, gameEvent.EndTime).Int();

        int currentSlotPosition = totalSlotValue % blueMarble.Slots.Length;

        BlueMarble.Slot slot = blueMarble.Slots[currentSlotPosition];
        // An item tile whose item cannot be made does not spend the roll's result: the request is dropped
        // with the result still open.
        Item? tileItem = null;
        if (slot.Type is BlueMarbleSlotType.Item or BlueMarbleSlotType.Paradise) {
            if (!session.GameEvent.Mapleopoly.ResultPending) {
                return;
            }
            tileItem = session.Field?.ItemDrop.CreateItem(slot.Item.ItemId, slot.Item.Rarity, slot.Item.Amount);
            if (tileItem == null) {
                Logger.Error("Mapleopoly tile item {ItemId} could not be created", slot.Item.ItemId);
                return;
            }
        }

        // One result per roll: without this a repeated result request pays the tile again.
        if (!session.GameEvent.Mapleopoly.TakeResult(slot.Type)) {
            return;
        }

        switch (slot.Type) {
            case BlueMarbleSlotType.Item:
            case BlueMarbleSlotType.Paradise:
                if (tileItem != null && !session.Item.Inventory.Add(tileItem, true)) {
                    session.Item.MailItem(tileItem);
                }
                break;
            case BlueMarbleSlotType.Backward:
                totalSlotValue = MapleopolyTurn.MoveBackward(totalSlotValue, slot.MoveAmount);
                break;
            case BlueMarbleSlotType.Forward:
                totalSlotValue += slot.MoveAmount;
                break;
            case BlueMarbleSlotType.WorldTour:
                totalSlotValue += blueMarble.Slots.Length;
                break;
            case BlueMarbleSlotType.GoToStarting:
                totalSlotValue += blueMarble.Slots.Length - currentSlotPosition;
                break;
            case BlueMarbleSlotType.Start:
                // Do nothing
                break;
            case BlueMarbleSlotType.Lose:
            case BlueMarbleSlotType.Roll:
            case BlueMarbleSlotType.Trap:
            default:
                Logger.Warning("Unhandled Mapleopoly slot type: {SlotType}", slot.Type);
                break;
        }

        int totalNewTrips = totalSlotValue / blueMarble.Slots.Length;
        RewardNewTrips(session, gameEvent, totalNewTrips);

        // The position a move tile leads to is kept: after a backward or forward result the client asks for the
        // result of the tile it walked to. RewardNewTrips stores the trip count itself.
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.MapleopolyTotalSlotCount, totalSlotValue);
        session.Send(MapleopolyPacket.Result(slot, totalSlotValue, freeRollValue));
    }

    private void RewardNewTrips(GameSession session, GameEvent gameEvent, int totalNewTrips) {
        int trips = session.GameEvent.Get(GameEventUserValueType.MapleopolyTotalTrips, gameEvent.Id, gameEvent.EndTime).Int();
        var blueMarble = (gameEvent.Metadata.Data as BlueMarble)!;
        while (trips < totalNewTrips) {
            trips++;

            // The reward of every lap (round 0), then the reward of this lap count, each at its amount.
            BlueMarble.Round? entry1 = blueMarble.Rounds.FirstOrDefault(entry => entry.RoundCount == 0);
            if (entry1 != default) {
                GiveTripReward(session, entry1.Item);
            }

            BlueMarble.Round? entry2 = blueMarble.Rounds.FirstOrDefault(entry => entry.RoundCount == trips);
            if (entry2 != default) {
                GiveTripReward(session, entry2.Item);
            }
        }

        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.MapleopolyTotalTrips, trips);
    }

    private void GiveTripReward(GameSession session, ItemComponent reward) {
        Item? item = session.Field?.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
        if (item == null) {
            Logger.Error("Mapleopoly lap reward {ItemId} could not be created", reward.ItemId);
            return;
        }
        session.Item.AddOrMail(item, true);
    }
}
