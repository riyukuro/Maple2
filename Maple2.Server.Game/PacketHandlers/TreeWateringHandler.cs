using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// StateTreeWatering (unit_140ad5280_statetreewateringcontext.cpp): the player finished watering (0) or cheering (1)
// the tree of the tree watering event. See TreeWateringEvent.
public class TreeWateringHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.TreeWatering;

    public override void Handle(GameSession session, IByteReader packet) {
        var action = (TreeWateringEvent.Action) packet.ReadByte();
        if (action is not (TreeWateringEvent.Action.Water or TreeWateringEvent.Action.Emotion)) {
            return;
        }
        if (session.Field == null || session.Player.IsDead
            || TreeWateringEvent.Find(session.FindEvent(GameEventType.TreeWatering)) is not { } gameEvent
            || gameEvent.Metadata.Data is not TreeWatering tree || session.Field.MapId != tree.FieldId) {
            return;
        }

        int waterCount = TreeWateringEvent.Count(session, gameEvent, TreeWateringEvent.Action.Water);
        if (action == TreeWateringEvent.Action.Water) {
            bool holdsPot = session.Item.Inventory.Find(tree.WaterPotItemId).Any();
            byte result = TreeWateringEvent.Water(tree, holdsPot, waterCount);
            if (result != TreeWateringEvent.Rewarded) {
                session.Send(TreeWateringPacket.Water(result));
                return;
            }
            if (!Reward(session, gameEvent, tree.WaterReward, action)) {
                return;
            }
            // The splash_plant trophies name the event's water pot as the code (92000091 20301391 ... 92000149 30001431).
            session.ConditionUpdate(ConditionType.splash_plant, codeLong: tree.WaterPotItemId);
            session.Send(TreeWateringPacket.Water(result, waterCount + 1, tree.WaterLimit));
            return;
        }

        int emotionCount = TreeWateringEvent.Count(session, gameEvent, TreeWateringEvent.Action.Emotion);
        byte? emotion = TreeWateringEvent.Emotion(tree, DateTime.Now, waterCount, emotionCount);
        if (emotion == null) {
            return;
        }
        if (emotion != TreeWateringEvent.Rewarded) {
            session.Send(TreeWateringPacket.Emotion(emotion.Value));
            return;
        }
        if (!Reward(session, gameEvent, tree.EmotionReward, action)) {
            return;
        }
        session.Send(TreeWateringPacket.Emotion(emotion.Value, emotionCount + 1, tree.EmotionLimit));
    }

    // The count and the reward are stored in one transaction (EventClaim): false, with nothing given and the count
    // unchanged, when it was not stored; no reply is sent then. Before, the count was stored alone first and the
    // reward given after it, so a reward whose write failed was lost with its count kept. A reward that cannot be
    // made still counts, as before, so it is not tried again.
    private bool Reward(GameSession session, GameEvent gameEvent, RewardItem reward, TreeWateringEvent.Action action) {
        var items = new List<Item>();
        if (reward.ItemId != 0 && reward.Amount > 0) {
            Item? item = session.Field?.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
            if (item == null) {
                Logger.Error("Tree watering event {EventId}: reward {ItemId} could not be made", gameEvent.Id, reward.ItemId);
            } else {
                items.Add(item);
            }
        }
        if (EventClaim.Give(session, items, [TreeWateringEvent.NextCount(session, gameEvent, action)]) == null) {
            Logger.Error("Tree watering event {EventId}: reward {ItemId} x{Amount} of {CharacterId} not stored, not given",
                gameEvent.Id, reward.ItemId, reward.Amount, session.CharacterId);
            return false;
        }
        return true;
    }
}
