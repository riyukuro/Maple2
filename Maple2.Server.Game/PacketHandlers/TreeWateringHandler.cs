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
            Reward(session, gameEvent, tree.WaterReward, action);
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
        Reward(session, gameEvent, tree.EmotionReward, action);
        session.Send(TreeWateringPacket.Emotion(emotion.Value, emotionCount + 1, tree.EmotionLimit));
    }

    // The count goes up first, so a reward that cannot be made is not given again.
    private void Reward(GameSession session, GameEvent gameEvent, RewardItem reward, TreeWateringEvent.Action action) {
        if (!TreeWateringEvent.AddCount(session, gameEvent, action)) {
            Logger.Error("Tree watering event {EventId}: the count of {CharacterId} was not stored", gameEvent.Id, session.CharacterId);
        }
        if (reward.ItemId == 0 || reward.Amount <= 0) {
            return;
        }
        Item? item = session.Field?.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
        if (item == null) {
            return;
        }
        session.Item.AddOrMail(item, true);
    }
}
