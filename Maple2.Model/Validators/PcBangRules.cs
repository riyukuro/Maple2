using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The PC bang dialog as the client reads it (client/ms2/uipcbangdialog.cpp, feature PCBang): a session at a PC bang
// has a grade above 0; its play time of the day counts toward the play bonus rows (PcBangBonus.xml), each claimed
// once a day when the time played reaches the row's minutes, and the gifts (PcBangGiftItem.xml) are received once a
// day. The client computes the time played as (server tick - start) + play time from the status the server sends
// (UIPcBangDialog::GetPlayTime) and enables a row with the time reached and its bit unset.
public static class PcBangRules {
    // Feature 0x3c (featuremanager.cpp): the client reads the info mode only with it on.
    public const string Feature = "PCBang";

    // UIPcBangDialog::OnLoad lays out m_timeBonus0..5.
    public const int MaxPlayBonusRows = 6;

    public static bool On(FeatureTable features) => features.IsOn(Feature);

    // The day key (yyyyMMdd) of the local day, the daily reset's day (WorldServer resets at local midnight).
    public static int Day(DateTime now) => now.Year * 10000 + now.Month * 100 + now.Day;

    // The record of the day: an earlier day's counts as a new day.
    public static PcBangRecord Today(PcBangRecord? record, DateTime now) {
        int day = Day(now);
        return record != null && record.Day == day ? record : new PcBangRecord(day, 0, 0, []);
    }

    // The record with the play from segmentStart to now added; a segment begun on an earlier day counts from the
    // day's start (the earlier day's part was that day's).
    public static PcBangRecord Fold(PcBangRecord? record, DateTime segmentStart, DateTime now) {
        PcBangRecord today = Today(record, now);
        DateTime from = segmentStart < now.Date ? now.Date : segmentStart;
        long ms = now > from ? (long) (now - from).TotalMilliseconds : 0;
        return today with { PlayMs = today.PlayMs + ms };
    }

    // The play bonus rows the client shows (the first MaxPlayBonusRows).
    public static IReadOnlyList<PcBangTable.PlayBonus> Rows(PcBangTable table) => table.PlayBonuses.Take(MaxPlayBonusRows).ToList();

    // PlayBonusItemGift::m_requiredTime: the row's minutes in milliseconds.
    public static long RequiredMs(PcBangTable.PlayBonus row) => row.RequireMinutes * 60_000L;

    public static bool IsClaimed(PcBangRecord record, int index) => index is >= 0 and < 32 && (record.Claimed & (1 << index)) != 0;

    public enum ClaimError { None, NotPcBang, NoRow, NotReached, Claimed }

    // A row is claimed at a PC bang, once a day, with its minutes played.
    public static ClaimError CanClaim(PcBangTable table, PcBangRecord today, int grade, int index) {
        if (grade <= 0) {
            return ClaimError.NotPcBang;
        }
        IReadOnlyList<PcBangTable.PlayBonus> rows = Rows(table);
        if (index < 0 || index >= rows.Count) {
            return ClaimError.NoRow;
        }
        if (IsClaimed(today, index)) {
            return ClaimError.Claimed;
        }
        return today.PlayMs < RequiredMs(rows[index]) ? ClaimError.NotReached : ClaimError.None;
    }

    public static PcBangRecord Claim(PcBangRecord today, int index) => today with { Claimed = today.Claimed | (1 << index) };

    public enum GiftError { None, NotPcBang, NoGift, Received }

    // A gift is received at a PC bang, once a day.
    public static GiftError CanReceiveGift(PcBangTable table, PcBangRecord today, int grade, int giftId) {
        if (grade <= 0) {
            return GiftError.NotPcBang;
        }
        if (!table.Gifts.ContainsKey(giftId)) {
            return GiftError.NoGift;
        }
        return today.Gifts.Contains(giftId) ? GiftError.Received : GiftError.None;
    }

    public static PcBangRecord ReceiveGift(PcBangRecord today, int giftId) => today with { Gifts = today.Gifts.Append(giftId).ToList() };

    // The buffs of the grade (PcBangBonus.xml <buffs level>); none below grade 1.
    public static IReadOnlyList<int> Buffs(PcBangTable table, int grade) =>
        grade > 0 && table.Buffs.TryGetValue(grade, out IReadOnlyList<int>? buffs) ? buffs : [];
}
