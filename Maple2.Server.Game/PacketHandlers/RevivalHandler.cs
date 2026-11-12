using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
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
        // With the room's default revival limit used up the client turns this button into "leave the dungeon"
        // (UIRevivalDialog::GetFirstButtonLabel) and does not send the request.
        bool inDungeon = session.Field is DungeonFieldManager;
        if (inDungeon && !session.Dungeon.HasDefaultRevivalLeft()) {
            Logger.Debug("Safe revive refused for {CharacterId}: dungeon revival limit used up", session.CharacterId);
            return;
        }
        // Revive player - this will handle moving to spawn point
        if (session.Player.Revive()) {
            session.Player.Tombstone = null;
            if (inDungeon) {
                session.Dungeon.CountDefaultRevival();
            }
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
        bool useVoucher = packet.ReadBool();

        // The client prices this button with CalcRevivalMeso(level, remaining daily count, 0)
        // (UIRevivalDialog::RefreshMesoButton); CalcRevivalPrice is a different, much smaller fee.
        int totalMaxCount = Lua.GetMesoRevivalDailyMaxCount();
        int remaining = Math.Max(totalMaxCount - session.Config.InstantReviveCount, 0);
        // The third argument is the client's meso revival event flag; this server runs no such event.
        ConstantsTable constants = ServerTableMetadata.ConstantsTable;
        bool homeField = session.Field.Metadata.Property.Type == MapType.Home;
        long cost = useVoucher ? 0 : RevivalRules.MesoCost(Lua.CalcRevivalMeso(session.Player.Value.Character.Level, remaining, 0),
            constants.MesoRevivalFeeReduceLimit, (float) session.Player.Stats.Values[SpecialAttribute.ReduceMesoRevivalFee].Multiplier(),
            homeField);
        int debuffCode = constants.MeratRevivalDebuffCode;
        bool debuffEnabled = debuffCode != 0 && session.Player.Buffs.EnumerateBuffs(debuffCode).Any(buff => buff.Enabled);
        MesoReviveRefusal refusal = RevivalRules.MesoRevive(
            dead: session.Player.IsDead && session.Player.Tombstone != null,
            noRevivalHere: session.Field.Metadata.Property.NoRevivalHere,
            buffBlocks: HasNoRevivalBuff(session),
            useVoucher: useVoucher,
            hasVoucher: session.Item.Inventory.Find(ItemTag.FreeReviveCoupon).Any(),
            usedToday: session.Config.InstantReviveCount,
            dailyMax: totalMaxCount,
            meso: session.Currency.Meso,
            cost: cost,
            debuffAllows: RevivalRules.DebuffAllows(debuffCode, debuffEnabled, session.Field.Metadata.Property.InfiniteMeretRevival),
            homeField: homeField);
        if (refusal != MesoReviveRefusal.None) {
            Logger.Debug("Instant revive refused for {CharacterId}: {Refusal}", session.CharacterId, refusal);
            return;
        }

        // The count goes up first: the revive sends it. Paid only once the revive has happened. The free
        // revive of a home field leaves the count alone.
        bool counts = RevivalRules.CountsTowardDailyMax(homeField && !useVoucher);
        if (counts) {
            session.Config.InstantReviveCount++;
        }
        if (!session.Player.Revive(true)) {
            if (counts) {
                session.Config.InstantReviveCount--;
            }
            return;
        }
        session.Player.Tombstone = null;
        if (useVoucher) {
            if (session.Item.Inventory.Consume([new IngredientInfo(ItemTag.FreeReviveCoupon, 1)])) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_revival_use_coupon));
            }
        } else {
            session.Currency.Meso -= cost;
        }
    }

    // Client UIRevivalDialog::SendRevivalMode1: the meret revive in place. The byte is the "m_coupon" checkbox:
    // 1 = pay with a free revive coupon (item tag 4), 0 = pay the merets the button shows.
    private void HandleCouponRevive(GameSession session, IByteReader packet) {
        bool useCoupon = packet.ReadBool();
        if (session.Field is null) return;

        ConstantsTable constants = ServerTableMetadata.ConstantsTable;
        int debuffCode = constants.MeratRevivalDebuffCode;
        Buff? debuff = debuffCode == 0 ? null : session.Player.Buffs.EnumerateBuffs(debuffCode).FirstOrDefault();
        int stacks = debuff?.Stacks ?? 0;
        // "Can pay" on the client is CClientPlatformBase::vfunc_40 (ms2clientplatformbase.cpp:316): the meret
        // balance alone. The red meret balance (vfunc_41) counts only with feature 0x43, which is off here.
        long cost = RevivalRules.MeretCost(Lua.CalcRevivalMerat(session.Player.Value.Character.Level, stacks),
            constants.MeratRevivalFeeReduceLimit, (float) session.Player.Stats.Values[SpecialAttribute.ReduceMeretRevivalFee].Multiplier());
        // The payment is tested first and taken only once the revive has happened.
        MeretReviveRefusal refusal = RevivalRules.MeretRevive(
            dead: session.Player.IsDead && session.Player.Tombstone != null,
            noRevivalHere: session.Field.Metadata.Property.NoRevivalHere,
            dungeonDisables: session.Field is DungeonFieldManager { DungeonMetadata.Limit.DisableMeretRevival: true },
            buffBlocks: HasNoRevivalBuff(session),
            // The constant LimitMeratRevival closes the meret revive for a whole locale (1 for NA in the client's
            // table; UIRevivalDialog::FUN_141abc930 hides the button). Ingest takes the client's row for it.
            localeClosed: constants.LimitMeratRevival != 0,
            debuffAllows: RevivalRules.DebuffAllows(debuffCode, debuffCode != 0 && session.Player.Buffs.EnumerateBuffs(debuffCode).Any(buff => buff.Enabled),
                session.Field.Metadata.Property.InfiniteMeretRevival),
            // The client sends the ticked box without a coupon only through its meret confirm box, which this
            // mode does not carry; a ticked box with no coupon held is refused and no merets are taken.
            useCoupon: useCoupon,
            hasCoupon: session.Item.Inventory.Find(ItemTag.FreeReviveCoupon).Any(),
            meret: session.Currency.Meret,
            cost: cost);
        if (refusal == MeretReviveRefusal.LackMeret) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_lack_merat));
        }
        if (refusal != MeretReviveRefusal.None) {
            Logger.Debug("Meret revive refused for {CharacterId}: {Refusal}", session.CharacterId, refusal);
            return;
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
    // (:441 merets or coupon, :496 mesos): an enabled buff with disableRevivalHere refuses with
    // s_msg_revival_merat_cannot_here_debuff. The return-to-town revive (mode 0) has no such test.
    private static bool HasNoRevivalBuff(GameSession session) {
        return BuffManager.BlocksRevival(session.Player.Buffs.EnumerateBuffs());
    }

    // Client StateSummonRevive: a dead summon owned by the player asks to revive; the fork has no summon revive state.
    private void HandleSummonRevive(GameSession session, IByteReader packet) {
        int objectId = packet.ReadInt();
        Logger.Debug("Revival of summon {ObjectId} from {CharacterId} not implemented", objectId, session.CharacterId);
    }
}
