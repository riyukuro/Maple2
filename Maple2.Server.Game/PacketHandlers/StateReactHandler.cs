using System.Numerics;
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

// CReactor::SendStateReact: the player finished the react cast on a reactor.
public class StateReactHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.StateReact;

    private enum Command : byte {
        React = 2,
    }

    // The client lets a player react within the reactor's reactDistance; the rest is room for movement.
    private const float DistanceSlack = 150f;

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        if (command != Command.React) {
            return;
        }

        int objectId = packet.ReadInt();
        if (session.Field == null || !session.Field.TryGetReactor(objectId, out FieldReactor? reactor)) {
            return;
        }
        if (Vector3.Distance(session.Player.Position, reactor.Position) > reactor.Metadata.ReactDistance + DistanceSlack) {
            return;
        }

        ReactorTable.State state = reactor.State;
        if (!ReactorRules.CanReact(state.Type, reactor.IsConnected(session.CharacterId))) {
            return;
        }
        if (!ReactorEvent.Check(session, reactor.Metadata, out GameEvent? gameEvent)) {
            return;
        }

        lock (session.Item) {
            // Every condition item must be held before any is taken (ReactorRules.Plan).
            (long Uid, int ItemId, int Rarity, int Amount)[] stacks = state.ConditionItems
                .Where(condition => condition.ItemId != 0)
                .Select(condition => condition.ItemId)
                .Distinct()
                .SelectMany(itemId => session.Item.Inventory.Find(itemId))
                .Select(item => (item.Uid, item.Id, item.Rarity, item.Amount))
                .ToArray();
            List<(long Uid, int Amount)>? plan = ReactorRules.Plan(state.ConditionItems, stacks);
            if (plan == null) {
                return;
            }

            // The plan was made from these stacks under this lock; test all of it again before the first take,
            // so a take cannot fail half way.
            if (plan.Any(entry => (session.Item.Inventory.Get(entry.Uid)?.Amount ?? 0) < entry.Amount)) {
                return;
            }
            foreach ((long uid, int amount) in plan) {
                if (!session.Item.Inventory.Consume(uid, amount)) {
                    Logger.Error("Reactor {ReactorId}: failed to take {Amount} of item {ItemUid} from {CharacterId}", reactor.Metadata.Id, amount, uid, session.CharacterId);
                    return;
                }
            }
        }

        if (state.Type == ReactorStateType.Connect) {
            reactor.Connect(session.CharacterId);
            return;
        }

        // Reward state.
        foreach (ReactorTable.Reward reward in reactor.Metadata.Rewards) {
            Item? item = session.Field.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
            if (item == null) {
                continue;
            }
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        session.Send(ReactorPacket.Reward(reactor.Metadata.Rewards));
        if (gameEvent != null && !ReactorEvent.AddReward(session, gameEvent)) {
            Logger.Error("Reactor event {EventId}: the count of {CharacterId} was not stored", gameEvent.Id, session.CharacterId);
        }
        reactor.Disconnect(session.CharacterId);
    }
}
