using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class RideManager {
    private readonly GameSession session;
    public Ride? Ride { get; set; }
    public RideOnType RideType => Ride?.Action.Type ?? RideOnType.None;


    private readonly ILogger logger = Log.Logger.ForContext<RideManager>();

    public RideManager(GameSession session) {
        this.session = session;
    }

    public bool Mount(Item item, bool flag = false) {
        if (Ride != null) {
            return false;
        }

        if (!session.RideMetadata.TryGet(item.Metadata.Property.Ride, out RideMetadata? metadata)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_item_invalid_function_item));
            return false;
        }

        if (item.Metadata.Limit.TransferType == TransferType.BindOnUse) {
            session.Item.Bind(item);
        }

        int objectId = FieldManager.NextGlobalId();
        var action = new RideOnActionDefault(item.Metadata.Property.Ride, objectId, item) { Flag = flag };
        Ride = new Ride(session.Player.ObjectId, metadata, action);
        session.Field?.Broadcast(RidePacket.Start(Ride));
        return true;
    }

    public bool Mount(AdditionalEffectMetadata additionalEffectMetadata) {
        if (session.Field is null) return false;
        if (Ride != null) {
            return false;
        }

        if (!session.Field.RideMetadata.TryGet(additionalEffectMetadata.Property.RideId, out RideMetadata? metadata)) {
            return false;
        }

        var action = new RideOnActionBattle(metadata.Id, FieldManager.NextGlobalId(), additionalEffectMetadata.Id, additionalEffectMetadata.Level);

        Ride = new Ride(session.Player.ObjectId, metadata, action);
        session.Field.Broadcast(RidePacket.Start(Ride));
        if (Ride.Metadata.Stats.Count > 0) {
            session.Stats.SetBattleMountStats(Ride.Metadata.Stats);
            session.Send(StatsPacket.Init(session.Player));
            session.Field.Broadcast(StatsPacket.Update(session.Player), session);
        }
        return true;

    }

    public void Dismount(RideOffType type) {
        Dismount(RideOffAction.Create(type));
    }

    public void Dismount(RideOffAction rideOffAction) {
        if (Ride == null) {
            return;
        }

        session.Field?.Broadcast(RidePacket.Stop(Ride.OwnerId, rideOffAction));

        Ride ride = Ride;
        Ride = null;

        if (ride.Action is RideOnActionBattle battle) {
            session.Buffs.Remove(battle.SkillId, session.Player.ObjectId);
        }
        if (ride.Metadata.Stats.Count > 0) {
            session.Stats.Refresh();
        }
        session.Config.LoadKeyTable();
    }
}
