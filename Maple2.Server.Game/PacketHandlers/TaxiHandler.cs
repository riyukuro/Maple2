using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using static Maple2.Model.Error.MigrationError;

namespace Maple2.Server.Game.PacketHandlers;

public class TaxiHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestTaxi;

    private enum Command : byte {
        DiscoverAtStation = 0,
        Taxi = 1,
        RotorsAir = 2,
        MesoAir = 3,
        MeretAir = 4,
        Discover = 5,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required MapMetadataStorage MapMetadata { private get; init; }
    public required MapEntityStorage EntityMetadata { private get; init; }
    public required TaxiRouteStorage TaxiRoute { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Taxi:
                HandleTaxi(session, packet);
                return;
            case Command.RotorsAir:
                HandleRotorsAirTaxi(session, packet);
                return;
            case Command.MesoAir:
                HandleMesoAirTaxi(session, packet);
                return;
            case Command.MeretAir:
                HandleMeretAirTaxi(session, packet);
                return;
            case Command.DiscoverAtStation: // the station's action key (actionkeyobject.cpp:800-802)
            case Command.Discover: // the station finder (autofindtaxistation.cpp:299-301)
                HandleDiscoverTaxi(session);
                return;
        }
    }

    private void HandleTaxi(GameSession session, IByteReader packet) {
        int mapId = packet.ReadInt();

        // The client rides only when alive and at its field's station (TaxiRules.StationReach); it waits for no answer.
        TaxiStation? station = session.Field?.Entities.Taxi;
        if (session.Player.IsDead || station == null
            || !InteractRules.InReach(session.Player.Position, station.Position, TaxiRules.StationReach(Constants.TaxiStationFindDistance))) {
            Logger.Debug("Refused taxi ride to {MapId}: dead or away from a station", mapId);
            return;
        }

        if (!CheckMapCashCall(session, mapId, MapMetadata)) {
            return;
        }

        if (!session.Player.Value.Unlock.Taxis.Contains(mapId)) {
            return;
        }

        if (!MapMetadata.TryGet(mapId, out MapMetadata? map)) {
            return;
        }

        MapEntityMetadata? entities = EntityMetadata.Get(map.XBlock);
        if (entities?.Taxi == null) {
            return;
        }

        // The fare the taxi box shows: CalcTaxiCharge of the client's portal hop count. With no path the client's fare
        // would be negative (CalcTaxiCharge(-1)); the server refuses that ride.
        int mapCount = TaxiRoute.Hops(session.Player.Value.Character.MapId, mapId);
        if (mapCount < 0) {
            return;
        }

        int cost = Lua.CalcTaxiCharge(mapCount, (ushort) session.Player.Value.Character.Level);

        if (!session.Currency.TryTakeMeso(cost)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_err_lack_meso));
            return;
        }

        session.ConditionUpdate(ConditionType.taxiuse);
        session.ConditionUpdate(ConditionType.taxifee, counter: cost);
        Vector3 position = entities.Taxi.Position.Offset(-VectorExtensions.BLOCK_SIZE, entities.Taxi.Rotation);
        Vector3 rotation = entities.Taxi.Rotation;
        session.Send(session.PrepareField(mapId, position: position, rotation: rotation)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(s_move_err_default));
    }

    // The client never sends mode 2 (its RequestTaxi senders use modes 0, 1, 3, 4 and 5); it took no payment, so it
    // is refused instead of moving the character for free.
    private void HandleRotorsAirTaxi(GameSession session, IByteReader packet) {
        int mapId = packet.ReadInt();
        Logger.Warning("Refused RequestTaxi mode 2 to {MapId} from {CharacterId}", mapId, session.CharacterId);
    }

    private float AirTaxiReduceRate(GameSession session) {
        return (float) session.Player.Stats.Values[SpecialAttribute.ReduceAirTaxiFee].Multiplier();
    }

    private void HandleMesoAirTaxi(GameSession session, IByteReader packet) {
        int mapId = packet.ReadInt();
        if (!CheckMapCashCall(session, mapId, MapMetadata)) {
            return;
        }

        bool premium = session.Config.IsPremiumClubActive();
        if (!TaxiRules.CanMesoRide(session.Player.Value.Unlock.Maps.Contains(mapId), premium)) {
            return; // the client offers merets or advanced walkie-talkies for a field never visited
        }

        long cost = TaxiRules.MesoFare(
            TaxiRules.AirFare(Lua.CalcAirTaxiCharge((ushort) session.Player.Value.Character.Level), AirTaxiReduceRate(session)),
            premium, session.FindEvent(GameEventType.FreeAirTaxi).Count > 0);

        if (cost > 0) {
            if (!session.Currency.TryTakeMeso(cost)) {
                session.Send(NoticePacket.MessageBox(StringCode.s_err_lack_meso));
                return;
            }
        }

        session.Send(session.PrepareField(mapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(s_move_err_default));
    }

    private void HandleMeretAirTaxi(GameSession session, IByteReader packet) {
        int mapId = packet.ReadInt();
        if (!CheckMapCashCall(session, mapId, MapMetadata)) {
            return;
        }

        long price = TaxiRules.AirFare(Constants.MeratAirTaxiPrice, AirTaxiReduceRate(session));
        if (!session.Currency.TryTakeMeret(price)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_err_lack_merat));
            return;
        }

        session.Send(session.PrepareField(mapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(s_move_err_default));
    }

    private void HandleDiscoverTaxi(GameSession session) {
        int mapId = session.Player.Value.Character.MapId;
        bool hasStation = MapMetadata.TryGet(mapId, out MapMetadata? map) && EntityMetadata.Get(map.XBlock)?.Taxi != null;
        if (!TaxiRules.CanDiscover(session.Player.Value.Unlock.Taxis.Contains(mapId), hasStation)) {
            return;
        }

        session.Player.Value.Unlock.Taxis.Add(mapId);
        session.Exp.AddExp(ExpType.taxi);
        session.Send(TaxiPacket.RevealTaxi(mapId));
        session.ConditionUpdate(ConditionType.taxifind);
    }

    public static bool CheckMapCashCall(GameSession session, int mapId, MapMetadataStorage mapMetadataStorage) {
        if (session.Field is null) return false;
        MapMetadataCashCall currentCashCall = session.Field.Metadata.CashCall;
        if (!currentCashCall.TaxiDeparture) {
            session.Send(NoticePacket.MessageBox(StringCode.s_err_cash_taxi_cannot_departure));
            return false;
        }

        if (!mapMetadataStorage.TryGet(mapId, out MapMetadata? map)) {
            return false;
        }

        // A field without its navmesh file cannot be created (FieldManager.Factory.Create): refuse the ride before the
        // fare is taken, with the move error the failed field would give.
        if (!Navigation.Exists(map.XBlock)) {
            session.Send(FieldEnterPacket.Error(s_move_err_default));
            return false;
        }

        // The client refuses a field with enterAdminLevel above 0 unless its debug mode is on
        // (fielddatacli.cpp FUN_140a55d20), and debug mode needs a GM account: s_field_limit_admin.
        if (!FieldEntryRules.AdminFieldAllowed(map.Limit.EnterAdminLevel, GmCheck.IsGm(session.Player.AdminPermissions))) {
            session.Send(NoticePacket.MessageBox(StringCode.s_field_limit_admin));
            return false;
        }

        if (!map.CashCall.TaxiDestination) {
            session.Send(NoticePacket.MessageBox(StringCode.s_err_cash_taxi_cannot_destination));
            return false;
        }

        return true;
    }
}
