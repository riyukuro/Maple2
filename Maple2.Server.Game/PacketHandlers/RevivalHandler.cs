using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class RevivalHandler : FieldPacketHandler {
    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

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
        if (HasNoRevivalBuff(session)) {
            return;
        }

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

    // Client UIRevivalDialog::SendRevivalMode1: the meret revive in place. The byte is the "m_coupon" checkbox:
    // 1 = pay with a free revive coupon (item tag 4), 0 = pay the merets the button shows.
    private void HandleCouponRevive(GameSession session, IByteReader packet) {
        bool useCoupon = packet.ReadBool();
        if (session.Field is null) return;
        if (!session.Player.IsDead || session.Player.Tombstone == null) {
            return;
        }
        if (session.Field.Metadata.Property.NoRevivalHere) {
            return;
        }
        if (session.Field is DungeonFieldManager { DungeonMetadata.Limit.DisableMeretRevival: true }) {
            return;
        }
        if (HasNoRevivalBuff(session)) {
            return;
        }

        ConstantsTable constants = ServerTableMetadata.ConstantsTable;
        // The constant LimitMeratRevival closes the meret revive for a whole locale (1 for NA in the client's
        // table; UIRevivalDialog::FUN_141abc930 hides the button). Ingest takes the client's row for it.
        if (constants.LimitMeratRevival != 0) {
            return;
        }
        // "Can pay" on the client is CClientPlatformBase::vfunc_40 (ms2clientplatformbase.cpp:316): the meret
        // balance alone. The red meret balance (vfunc_41) counts only with feature 0x43, which is off here.
        int debuffCode = constants.MeratRevivalDebuffCode;
        Buff? debuff = debuffCode == 0 ? null : session.Player.Buffs.EnumerateBuffs(debuffCode).FirstOrDefault();
        int stacks = debuff?.Stacks ?? 0;
        if (!RevivalRules.DebuffAllows(debuffCode, stacks, debuff?.Metadata.Property.MaxCount ?? 0,
                session.Field.Metadata.Property.InfiniteMeretRevival)) {
            return;
        }

        // The payment is tested first and taken only once the revive has happened.
        long cost = 0;
        if (useCoupon) {
            // The client sends the ticked box without a coupon only through its meret confirm box, which this
            // mode does not carry; a ticked box with no coupon held is refused and no merets are taken.
            if (!session.Item.Inventory.Find(ItemTag.FreeReviveCoupon).Any()) {
                return;
            }
        } else {
            float reduceRate = (float) session.Player.Stats.Values[SpecialAttribute.ReduceMeretRevivalFee].Multiplier();
            cost = RevivalRules.MeretCost(Lua.CalcRevivalMerat(session.Player.Value.Character.Level, stacks),
                constants.MeratRevivalFeeReduceLimit, reduceRate);
            if (session.Currency.Meret < cost) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_lack_merat));
                return;
            }
        }

        if (!session.Player.Revive(true)) {
            return;
        }
        session.Player.Tombstone = null;
        if (useCoupon) {
            if (session.Item.Inventory.Consume([new IngredientInfo(ItemTag.FreeReviveCoupon, 1)])) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_revival_use_coupon));
            }
        } else {
            session.Currency.Meret -= cost;
        }
        if (debuffCode != 0) {
            session.Player.Buffs.AddBuff(session.Player, session.Player, debuffCode, 1, session.Field.FieldTick);
        }
    }

    // Client UIRevivalDialog::FUN_141abdab0 (uirevivaldialog.cpp:801), called by both revive-in-place buttons
    // (:441 merets or coupon, :496 mesos): a buff with disableRevivalHere refuses with
    // s_msg_revival_merat_cannot_here_debuff. The return-to-town revive (mode 0) has no such test.
    private static bool HasNoRevivalBuff(GameSession session) {
        return RevivalRules.BuffBlocks(session.Player.Buffs.EnumerateBuffs().Select(buff => buff.Metadata.Property.DisableRevivalHere));
    }

    // Client StateSummonRevive: a dead summon owned by the player asks to revive; the fork has no summon revive state.
    private void HandleSummonRevive(GameSession session, IByteReader packet) {
        int objectId = packet.ReadInt();
        Logger.Debug("Revival of summon {ObjectId} from {CharacterId} not implemented", objectId, session.CharacterId);
    }
}
