using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
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

    // Hide and Seek: the hider becomes the ride's object (RideOnAction type 3, the client's RideOnActionHideAndSeek,
    // read with the base fields only). A ride the player is on ends first. The object's health is the game's own
    // (HideAndSeekState), so the ride's stats are not put on the player.
    public bool MountObject(RideMetadata metadata) {
        if (session.Field is null) return false;
        if (Ride != null) {
            Dismount(RideOffType.HideAndSeek);
        }

        var action = new RideOnActionObject(metadata.Id, FieldManager.NextGlobalId());
        Ride = new Ride(session.Player.ObjectId, metadata, action);
        session.Field.Broadcast(RidePacket.Start(Ride));
        return true;
    }

    public void Dismount(RideOffType type) {
        Dismount(RideOffAction.Create(type));
    }

    public void Dismount(RideOffAction rideOffAction) {
        if (Ride == null) {
            return;
        }

        if (session.Field != null && Ride.OwnerId == session.Player.ObjectId) {
            ClearPassengers(session.Field, Ride);
        }
        session.Field?.Broadcast(RidePacket.Stop(Ride.OwnerId, rideOffAction));
        EndRide();
    }

    // RequestRide mode 2: the owner swaps its mount item for another. The client's reader of the answer gets off
    // and summons the new item, which arrives as a new Start; so the old ride ends here like a dismount (the
    // passengers get off, a ride's stats and keys go back).
    public bool Change(int rideId, long itemUid) {
        if (Ride == null || session.Field == null
            || !RideShare.CanChange(Ride.OwnerId == session.Player.ObjectId, Ride.Action.Type)) {
            return false;
        }

        ClearPassengers(session.Field, Ride);
        session.Field.Broadcast(RidePacket.Change(session.Player.ObjectId, rideId, itemUid));
        EndRide();
        return true;
    }

    private void EndRide() {
        if (Ride == null) {
            return;
        }

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

    /// <summary>
    /// The player left the field. An owner keeps the mount but its passengers stay behind; a passenger gets off.
    /// </summary>
    public void LeaveField(FieldManager field) {
        if (Ride == null) {
            return;
        }

        if (Ride.OwnerId == session.Player.ObjectId) {
            ClearPassengers(field, Ride);
            return;
        }

        int seat = Array.IndexOf(Ride.Passengers, session.Player.ObjectId);
        if (seat >= 0) {
            Ride.Passengers[seat] = 0;
            field.Broadcast(RidePacket.Leave(Ride.OwnerId, session.Player.ObjectId));
        }
        Ride = null;
    }

    /// <summary>
    /// The player entered a field: a mount the field does not allow ends with ride-off type DisableField.
    /// </summary>
    public void EnterField(FieldManager field) {
        if (Ride == null || !RideShare.DismountsOnEnter(true, field.Metadata.Limit.Ride)) {
            return;
        }

        // The rider is not in the field's players yet, so a broadcast would miss it. In KMS2 the rider itself gets
        // the Stop just before its own FieldAddUser; the players already there never saw the mount.
        session.Send(RidePacket.Stop(Ride.OwnerId, RideOffAction.Create(RideOffType.DisableField)));
        EndRide();
    }

    // RideConsumeEp: the owner dashes on its mount.
    public void ConsumeDashStamina() {
        if (Ride == null || session.Field == null) {
            return;
        }
        int amount = RideShare.DashStamina(Ride.OwnerId == session.Player.ObjectId, Ride.Metadata.Basic.RunXStamina);
        session.Player.ConsumeStamina(amount);
    }

    private static void ClearPassengers(FieldManager field, Ride ride) {
        if (ride.Passengers.All(objectId => objectId == 0)) {
            return;
        }

        field.Broadcast(RidePacket.ChangeShared(ride.OwnerId));
        foreach (int objectId in RideShare.ClearSeats(ride.Passengers)) {
            if (field.TryGetPlayer(objectId, out FieldPlayer? passenger) && passenger.Session.Ride.Ride == ride) {
                passenger.Session.Ride.Ride = null;
            }
        }
    }
}
