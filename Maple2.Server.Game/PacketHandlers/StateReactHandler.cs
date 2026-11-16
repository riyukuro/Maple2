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
        if (session.Field == null) {
            return;
        }
        long now = session.Field.FieldTick;
        long? lastReact = session.LastReactTick;
        session.LastReactTick = now;
        if (!session.Field.TryGetReactor(objectId, out FieldReactor? reactor)) {
            return;
        }
        LogTooSoon(session, reactor, lastReact, now);
        if (Vector3.Distance(session.Player.Position, reactor.Position) > reactor.Metadata.ReactDistance + DistanceSlack) {
            return;
        }

        ReactorTable.State state = reactor.State;
        if (!ReactorRules.CanReact(state.Type, reactor.IsConnected(session.CharacterId))) {
            return;
        }
        // A full connect state takes nobody more, before any item is taken (ReactorRules.MaxConnected).
        if (state.Type == ReactorStateType.Connect && reactor.ConnectedCount >= ReactorRules.MaxConnected) {
            return;
        }
        if (!ReactorEvent.Check(session, reactor.Metadata, out GameEvent? gameEvent)) {
            return;
        }

        // An event reactor's reward state takes its condition items in the claim's transaction (ReactorEvent.Claim),
        // so a claim not stored takes nothing; the other states take them here.
        bool eventReward = gameEvent != null && state.Type != ReactorStateType.Connect;
        List<(long Uid, int Amount)>? plan;
        lock (session.Item) {
            // Every condition item must be held before any is taken (ReactorRules.Plan).
            (long Uid, int ItemId, int Rarity, int Amount)[] stacks = state.ConditionItems
                .Where(condition => condition.ItemId != 0)
                .Select(condition => condition.ItemId)
                .Distinct()
                .SelectMany(itemId => session.Item.Inventory.Find(itemId))
                .Select(item => (item.Uid, item.Id, item.Rarity, item.Amount))
                .ToArray();
            plan = ReactorRules.Plan(state.ConditionItems, stacks);
            if (plan == null) {
                return;
            }

            // The plan was made from these stacks under this lock; test all of it again before the first take,
            // so a take cannot fail half way.
            if (plan.Any(entry => (session.Item.Inventory.Get(entry.Uid)?.Amount ?? 0) < entry.Amount)) {
                return;
            }
        }
        // The other states' take is stored at once (ConsumeNow, outside the bag lock as its purchase save needs): a
        // reward state gives its items as rows at once, so a take in memory only came back on a crash before the
        // session save, with the reward kept. A take that is not stored gives nothing.
        if (!eventReward && plan.Count > 0 && !session.Item.Inventory.ConsumeNow(plan)) {
            Logger.Error("Reactor {ReactorId}: the condition items of {CharacterId} were not taken", reactor.Metadata.Id, session.CharacterId);
            return;
        }

        if (state.Type == ReactorStateType.Connect) {
            reactor.Connect(session.CharacterId);
            return;
        }

        // Reward state.
        var items = new List<Item>();
        foreach (ReactorTable.Reward reward in reactor.Metadata.Rewards) {
            Item? item = session.Field.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
            if (item != null) {
                items.Add(item);
            }
        }
        if (gameEvent != null) {
            // An event reactor's reward and the day's count are stored together (ReactorEvent.Claim); when they are
            // not, nothing is given and the count stays.
            if (!ReactorEvent.Claim(session, gameEvent, items, plan)) {
                reactor.Disconnect(session.CharacterId);
                return;
            }
        } else {
            foreach (Item item in items) {
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
        }
        session.Send(ReactorPacket.Reward(reactor.Metadata.Rewards));
        reactor.Disconnect(session.CharacterId);
    }

    // A react the client could not have sent yet: its cast bar runs the reactor's reactTimeMSec after the last react.
    // Logged only until honest play shows none; the react is handled as before.
    private void LogTooSoon(GameSession session, FieldReactor reactor, long? lastReact, long now) {
        if (lastReact is not { } last || !ReactorRules.ReactTooSoon(last, now, reactor.Metadata.ReactTimeMs)) {
            return;
        }
        if (session.Config.ReactLog.Count(now) is > 0 and int unlogged) {
            Logger.Information("StateReact before its cast (logged only): {Elapsed} ms since the last react, a {CastTime} ms cast, reactor {ReactorId} ({ObjectId}), {Unlogged} since the last line, {Player}",
                now - last, reactor.Metadata.ReactTimeMs, reactor.Metadata.Id, reactor.ObjectId, unlogged, session.PlayerName);
        }
    }
}
