using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class RevivalHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Revival;

    private enum Command : byte {
        SafeRevive = 0,
        CouponRevive = 1,
        InstantRevive = 2,
        SummonRevive = 3,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.SafeRevive:
                HandleSafeRevive(session);
                return;
            case Command.InstantRevive:
                HandleInstantRevive(session, packet);
                return;
            case Command.CouponRevive:
                HandleCouponRevive(session, packet);
                return;
            case Command.SummonRevive:
                HandleSummonRevive(session, packet);
                return;
        }
    }

    /// <summary>
    /// Handles normal revival (from tombstone hits or manual revival)
    /// </summary>
    private void HandleSafeRevive(GameSession session) {
        if (session.Field is null) return;
        if (session.Field.Metadata.Property.NoRevivalHere) {
            Logger.Debug("Safe revive refused for {CharacterId}: map {MapId} has NoRevivalHere", session.CharacterId, session.Field.MapId);
            return;
        }
        // Revive player - this will handle moving to spawn point
        if (session.Player.Revive()) {
            session.Player.Tombstone = null;
        } else {
            Logger.Debug("Safe revive ignored for {CharacterId}: dead {Dead}, tombstone {Tombstone}",
                session.CharacterId, session.Player.IsDead, session.Player.Tombstone != null);
        }
    }

    /// <summary>
    /// Handles instant revival (using mesos or voucher)
    /// </summary>
    private void HandleInstantRevive(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        if (session.Field.Metadata.Property.NoRevivalHere) {
            return;
        }

        bool useVoucher = packet.ReadBool();

        if (useVoucher) {
            if (!session.Item.Inventory.Consume([new IngredientInfo(ItemTag.FreeReviveCoupon, 1)])) {
                // Send error packet?
                return;
            }
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_revival_use_coupon));
        } else {
            int totalMaxCount = Lua.GetMesoRevivalDailyMaxCount();
            if (session.Config.InstantReviveCount >= totalMaxCount) {
                return;
            }

            // The client prices this button with CalcRevivalMeso(level, remaining daily count, 0)
            // (UIRevivalDialog::RefreshMesoButton); CalcRevivalPrice is a different, much smaller fee.
            int remaining = Math.Max(totalMaxCount - session.Config.InstantReviveCount, 0);
            int cost = Lua.CalcRevivalMeso(session.Player.Value.Character.Level, remaining, 0);
            if (session.Currency.Meso < cost) {
                // Client sends error
                return;
            }
            session.Currency.Meso -= cost;
        }

        session.Config.InstantReviveCount++;
        if (session.Player.Revive(true)) {
            session.Player.Tombstone = null;
        }
    }

    // Client UIRevivalDialog::SendRevivalMode1: revive in place, with the coupon value from FUN_141abc9d0.
    // The meaning of that value is not settled, so nothing is consumed here.
    private void HandleCouponRevive(GameSession session, IByteReader packet) {
        byte coupon = packet.ReadByte();
        Logger.Debug("Revival mode 1 (coupon {Coupon}) from {CharacterId} not implemented", coupon, session.CharacterId);
    }

    // Client StateSummonRevive: a dead summon owned by the player asks to revive; the fork has no summon revive state.
    private void HandleSummonRevive(GameSession session, IByteReader packet) {
        int objectId = packet.ReadInt();
        Logger.Debug("Revival of summon {ObjectId} from {CharacterId} not implemented", objectId, session.CharacterId);
    }
}
