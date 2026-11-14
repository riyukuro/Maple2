using System.Net;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

// The session's PC bang state (PcBangRules): the grade (0 = not at a PC bang; from Target.PcBangNetworks at login,
// or the pcbang command) and the start of the play segment not yet folded into the account's day record. The
// record (Account.PcBang) is stored with the player, with the items its claims give.
public sealed class PcBangManager {
    private readonly GameSession session;
    private readonly ILogger logger = Log.Logger.ForContext<PcBangManager>();
    private readonly object gate = new();

    public int Grade { get; private set; }
    private DateTime segmentStart;
    // The server tick the client's play time counts from (UIPcBangDialog::GetPlayTime).
    private int startTick;

    public PcBangManager(GameSession session) {
        this.session = session;
    }

    private PcBangTable Table => session.ServerTableMetadata.PcBangTable;
    public bool On => PcBangRules.On(session.ServerTableMetadata.FeatureTable);

    // The grade of a client address: 1 inside a PC bang network.
    public static int GradeOf(IPAddress? address, IReadOnlyList<IPNetwork> networks) {
        if (address == null) {
            return 0;
        }
        if (address.IsIPv4MappedToIPv6) {
            address = address.MapToIPv4();
        }
        return networks.Any(network => network.Contains(address)) ? 1 : 0;
    }

    // At login: the grade from the address, and the dialog's info (closed).
    public void Load() {
        if (!On) {
            return;
        }
        lock (gate) {
            Grade = GradeOf(session.RemoteAddress, Target.PcBangNetworks);
            segmentStart = DateTime.Now;
        }
        SendInfo(false);
    }

    // The play since the segment's start added to the day's record (at a PC bang only), the segment begun again.
    public PcBangRecord Fold(DateTime now) {
        lock (gate) {
            Account account = session.Player.Value.Account;
            account.PcBang = Grade > 0
                ? PcBangRules.Fold(account.PcBang, segmentStart, now)
                : PcBangRules.Today(account.PcBang, now);
            segmentStart = now;
            startTick = Environment.TickCount64.Truncate32();
            return account.PcBang;
        }
    }

    public void SendInfo(bool show) {
        PcBangRecord today = Fold(DateTime.Now);
        session.Send(PcBangPacket.Info(Grade, startTick, today.PlayMs, today.Claimed, session.ServerTableMetadata.ConstantsTable.PCBangAdditionalEffectID,
            PcBangRules.Rows(Table), Table.Gifts.Values.OrderBy(gift => gift.Id).ToList(), show));
    }

    public void SendStatus() {
        PcBangRecord today = Fold(DateTime.Now);
        session.Send(PcBangPacket.Status(Grade, startTick, today.PlayMs, today.Claimed));
    }

    // A new grade: the play so far is folded with the old one, the field sees the grade (0xB5), the grade's buffs
    // replace the old ones and the dialog gets the status.
    public void SetGrade(int grade) {
        if (!On) {
            return;
        }
        grade = Math.Clamp(grade, 0, byte.MaxValue);
        int old;
        lock (gate) {
            Fold(DateTime.Now);
            old = Grade;
            Grade = grade;
        }
        if (session.Field != null) {
            session.Field.Broadcast(PcBangPacket.SetPcBang(session.Player.ObjectId, grade));
            foreach (int buffId in PcBangRules.Buffs(Table, old).Except(PcBangRules.Buffs(Table, grade))) {
                session.Player.Buffs.Remove(buffId, session.Player.ObjectId);
            }
            RefreshBuffs();
        }
        SendStatus();
    }

    // The grade's buffs (field entry, a grade change).
    public void RefreshBuffs() {
        if (!On || Grade <= 0 || session.Field == null) {
            return;
        }
        foreach (int buffId in PcBangRules.Buffs(Table, Grade)) {
            if (session.Player.Buffs.EnumerateBuffs(buffId).Count > 0) {
                continue;
            }
            session.Player.Buffs.AddBuff(session.Player, session.Player, buffId, 1, session.Field.FieldTick);
        }
    }

    // A play bonus row: claimed once a day at a PC bang with its minutes played and room in the inventory.
    public void Claim(int index) {
        if (!On) {
            return;
        }
        PcBangRecord today = Fold(DateTime.Now);
        PcBangRules.ClaimError error = PcBangRules.CanClaim(Table, today, Grade, index);
        if (error == PcBangRules.ClaimError.NotPcBang) {
            session.Send(PcBangPacket.Notice(PcBangPacket.NoticeCode.NotPcBang));
            return;
        }
        if (error != PcBangRules.ClaimError.None) {
            SendStatus();
            return;
        }
        PcBangTable.PlayBonus row = PcBangRules.Rows(Table)[index];
        Item? item = session.Field?.ItemDrop.CreateItem(row.ItemId, row.Rarity, row.Count);
        if (item == null || !session.Item.Inventory.CanAdd(item)) {
            logger.Debug("PcBang: row {Index} item {ItemId} not given CharacterId={CharacterId}", index, row.ItemId, session.CharacterId);
            SendStatus();
            return;
        }
        PcBangRecord claimed;
        lock (gate) {
            claimed = PcBangRules.Claim(session.Player.Value.Account.PcBang ?? today, index);
            session.Player.Value.Account.PcBang = claimed;
        }
        session.Item.Inventory.Add(item, true);
        session.Send(PcBangPacket.Played(index, claimed.Claimed));
    }

    // A gift: received once a day at a PC bang, not in a tutorial field.
    public void ReceiveGift(int giftId) {
        if (!On) {
            return;
        }
        if (Grade <= 0) {
            session.Send(PcBangPacket.Notice(PcBangPacket.NoticeCode.NotPcBang));
            return;
        }
        if (session.Field == null || session.Field.Metadata.Property.TutorialType != 0) {
            session.Send(PcBangPacket.Notice(PcBangPacket.NoticeCode.Tutorial));
            return;
        }
        PcBangRecord today = Fold(DateTime.Now);
        if (PcBangRules.CanReceiveGift(Table, today, Grade, giftId) != PcBangRules.GiftError.None) {
            return;
        }
        PcBangTable.Gift gift = Table.Gifts[giftId];
        Item? item = session.Field.ItemDrop.CreateItem(gift.ItemId, gift.Rarity, gift.Count);
        if (item == null || !session.Item.Inventory.CanAdd(item)) {
            return;
        }
        lock (gate) {
            session.Player.Value.Account.PcBang = PcBangRules.ReceiveGift(session.Player.Value.Account.PcBang ?? today, giftId);
        }
        session.Item.Inventory.Add(item, true);
        session.Send(PcBangPacket.Reward(giftId));
    }
}
