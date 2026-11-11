using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class DungeonRoomRulesTests {
    [TestCase(false, 1, 0, 4, false, DungeonRoomRules.CreateVerdict.Ok)]
    [TestCase(false, 1, 4, 4, false, DungeonRoomRules.CreateVerdict.UnderMinUserCount)]
    [TestCase(false, 1, 4, 4, true, DungeonRoomRules.CreateVerdict.Ok)]
    [TestCase(true, 4, 0, 4, false, DungeonRoomRules.CreateVerdict.Ok)]
    [TestCase(true, 5, 0, 4, false, DungeonRoomRules.CreateVerdict.OverMaxUserCount)]
    [TestCase(true, 1, 0, 1, false, DungeonRoomRules.CreateVerdict.OverMaxUserCount)]
    [TestCase(true, 2, 4, 10, false, DungeonRoomRules.CreateVerdict.Ok)]
    public void Create(bool withParty, int partySize, int min, int max, bool gm, DungeonRoomRules.CreateVerdict expected) {
        Assert.That(DungeonRoomRules.CanCreate(withParty, partySize, min, max, gm), Is.EqualTo(expected));
    }

    // The guild raid rows of the played client's table: limitDayOfWeeks="1,2,3,4,6,7" (no Thursday).
    private static readonly DayOfWeek[] GuildRaidDays = [
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday,
    ];

    [TestCase(DayOfWeek.Sunday, true)]
    [TestCase(DayOfWeek.Wednesday, true)]
    [TestCase(DayOfWeek.Thursday, false)]
    [TestCase(DayOfWeek.Saturday, true)]
    public void OpenOnDay_TheListedDays(DayOfWeek today, bool open) {
        Assert.That(DungeonRoomRules.OpenOnDay(GuildRaidDays, today), Is.EqualTo(open));
    }

    [Test]
    public void OpenOnDay_NoListIsEveryDay() {
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) {
            Assert.That(DungeonRoomRules.OpenOnDay([], day), Is.True);
        }
    }

    // The ingest's day numbers are the client's: Sunday 1 to Saturday 7 (CMsTime::GetDayOfWeek = tm_wday + 1).
    [Test]
    public void ParserDayNumbersAreTheClients() {
        Assert.That((int) Maple2.File.Parser.Enum.DayOfWeek.sun, Is.EqualTo(1));
        Assert.That((int) Maple2.File.Parser.Enum.DayOfWeek.thu, Is.EqualTo(5));
        Assert.That((int) Maple2.File.Parser.Enum.DayOfWeek.sat, Is.EqualTo(7));
    }

    [Test]
    public void CreateBytes() {
        // u8 mode is read by the handler; then i32 dungeon id, bool party, i32 portal id, bool ticket.
        var writer = new ByteWriter();
        writer.WriteInt(23000001);
        writer.WriteBool(true);
        writer.WriteInt(7);
        writer.WriteBool(false);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // HasCountLeft: the client's DungeonRecordCli::HasCountLeft on the record fields the server writes.
    private static DungeonRecord Record(byte taken, long expire, byte subClears = 0, long subExpire = 0, byte bonus = 0) =>
        new(1) { UnionClears = taken, UnionCooldownTimestamp = expire, UnionSubClears = subClears, UnionSubCooldownTimestamp = subExpire, ExtraSubClears = bonus };

    [TestCase(0, true)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    public void HasCountLeft_TheRewardsTakenBeforeTheExpire(int taken, bool left) {
        Assert.That(DungeonRoomRules.HasCountLeft(Record((byte) taken, 200), 3, 0, 100), Is.EqualTo(left));
    }

    // A room without a subRewardCount (the Colosseum row has none) has no daily limit: a new record, and one that
    // has taken a reward today, still count the next clear. The clear's reward step uses this rule.
    [Test]
    public void HasCountLeft_NoSubRewardCountIsNoDailyLimit() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.HasCountLeft(new DungeonRecord(1), 10, 0, 100), Is.True);
            Assert.That(DungeonRoomRules.HasCountLeft(Record(2, 200, subClears: 2, subExpire: 150), 10, 0, 100), Is.True);
        });
    }

    [Test]
    public void HasCountLeft_AtTheExpireTheCountIsBack() {
        Assert.That(DungeonRoomRules.HasCountLeft(Record(3, 200), 3, 0, 200), Is.True);
    }

    [Test]
    public void HasCountLeft_TheBonusCountAddsUntilTheExpire() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.HasCountLeft(Record(3, 200, bonus: 1), 3, 0, 100), Is.True);
            Assert.That(DungeonRoomRules.HasCountLeft(Record(4, 200, bonus: 1), 3, 0, 100), Is.False);
        });
    }

    [Test]
    public void HasCountLeft_TheSubCountStopsUntilItsOwnExpire() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.HasCountLeft(Record(1, 200, subClears: 1, subExpire: 150), 3, 1, 100), Is.False);
            Assert.That(DungeonRoomRules.HasCountLeft(Record(1, 200, subClears: 1, subExpire: 150), 3, 1, 150), Is.True);
            Assert.That(DungeonRoomRules.HasCountLeft(Record(1, 200, subClears: 1, subExpire: 150), 3, 0, 100), Is.True);
        });
    }

    [Test]
    public void HasCountLeft_NoRecordHasACount() {
        Assert.That(DungeonRoomRules.HasCountLeft(null, 1, 0, 100), Is.True);
    }

    // BonusCountNow: the bought counts until the record's expire (DungeonRecordCli::GetBonusCountNow).
    [Test]
    public void BonusCountNow_CountsUntilTheExpire() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.BonusCountNow(Record(0, 200, bonus: 2), 100), Is.EqualTo(2));
            Assert.That(DungeonRoomRules.BonusCountNow(Record(0, 200, bonus: 2), 200), Is.EqualTo(0));
            Assert.That(DungeonRoomRules.BonusCountNow(null, 100), Is.EqualTo(0));
        });
    }

    // The played client's coupon 2 (rates 1, 2, 4, 8, 16) on a room with base count 1, and coupon 1 (rate 1,
    // maxExtraCount 8) on a room with base count 10.
    private static DungeonExtraRewardTicket Ticket(int baseCount, int max, params int[] rates) =>
        new(1, baseCount, ItemTag.DungeonRewardTicketA, 20300478, max, rates, true);

    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(4, 16)]
    [TestCase(9, 16)]
    [TestCase(-1, 1)]
    public void ExtraTicketCost_TheRateAtTheBoughtCount(int bonus, int cost) {
        Assert.That(DungeonRoomRules.ExtraTicketCost(Ticket(1, 0, 1, 2, 4, 8, 16), bonus), Is.EqualTo(cost));
    }

    [Test]
    public void ExtraTicketCost_BaseCountTimesRate() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.ExtraTicketCost(Ticket(10, 8, 1), 3), Is.EqualTo(10));
            Assert.That(DungeonRoomRules.ExtraTicketCost(Ticket(10, 8), 0), Is.EqualTo(0));
        });
    }

    [Test]
    public void CanAddExtraReward_Verdicts() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.CanAddExtraReward(null, 0), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.NoTicket));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(0, 8, 1), 0), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.NoTicket));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(10, 8), 0), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.NoTicket));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(10, 8, 1) with { TicketTag = ItemTag.None }, 0),
                Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.NoTicket));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(10, 8, 1), 7), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.Ok));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(10, 8, 1), 8), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.AtMax));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(1, 0, 1, 2), 254), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.Ok));
            Assert.That(DungeonRoomRules.CanAddExtraReward(Ticket(1, 0, 1, 2), 255), Is.EqualTo(DungeonRoomRules.ExtraRewardVerdict.AtMax));
        });
    }

    // PartyBlockingLimit: a member's enter state of 10 and above blocks the party (CPartyManager::CanEnterDungeonRoom).
    private static IReadOnlyDictionary<int, DungeonEnterLimit> Limits(int dungeonId, DungeonEnterLimit limit) =>
        new Dictionary<int, DungeonEnterLimit> { [dungeonId] = limit };

    [TestCase(DungeonEnterLimit.Rookie, false)]
    [TestCase(DungeonEnterLimit.Veteran, false)]
    [TestCase(DungeonEnterLimit.MinLevel, true)]
    [TestCase(DungeonEnterLimit.RecommendedWeapon, true)]
    public void PartyBlockingLimit_TenAndAboveBlocks(DungeonEnterLimit limit, bool blocks) {
        DungeonEnterLimit? result = DungeonRoomRules.PartyBlockingLimit(new[] { Limits(5, limit) }, 5);
        Assert.That(result, blocks ? Is.EqualTo(limit) : Is.Null);
    }

    [Test]
    public void PartyBlockingLimit_TheFirstBlockingMemberAndOnlyThisRoom() {
        var members = new[] {
            Limits(5, DungeonEnterLimit.Veteran),
            Limits(6, DungeonEnterLimit.Vip),
            Limits(5, DungeonEnterLimit.Gearscore),
            Limits(5, DungeonEnterLimit.Achievement),
        };
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.PartyBlockingLimit(members, 5), Is.EqualTo(DungeonEnterLimit.Gearscore));
            Assert.That(DungeonRoomRules.PartyBlockingLimit(members, 7), Is.Null);
        });
    }

    private static DungeonRecord HelperRecord(int clears, bool veteran) =>
        new(1) { TotalClears = clears, Flag = veteran ? DungeonRecordFlag.Veteran : DungeonRecordFlag.None };

    // CanClaimHelperReward: a veteran's record with the room's required clears (DungeonRecordManagerCli::CanClaimHelperReward).
    [TestCase(5, true, 5, true)]
    [TestCase(9, true, 5, true)]
    [TestCase(4, true, 5, false)]
    [TestCase(9, false, 5, false)]
    [TestCase(9, true, 0, false)]
    public void CanClaimHelperReward_NeedsAVeteranWithTheClears(int clears, bool veteran, int requireCount, bool claim) {
        Assert.That(DungeonRoomRules.CanClaimHelperReward(HelperRecord(clears, veteran), requireCount), Is.EqualTo(claim));
    }

    [Test]
    public void CanClaimHelperReward_NotWithoutARecord() {
        Assert.That(DungeonRoomRules.CanClaimHelperReward(null, 5), Is.False);
    }

    // WithOtherRookie: another player of the clear is not a veteran; the player's own state does not count.
    [Test]
    public void WithOtherRookie_CountsOtherPlayersOnly() {
        Assert.Multiple(() => {
            Assert.That(DungeonRoomRules.WithOtherRookie(1, new Dictionary<long, bool> { [1] = true, [2] = false }), Is.True);
            Assert.That(DungeonRoomRules.WithOtherRookie(1, new Dictionary<long, bool> { [1] = true, [2] = true }), Is.False);
            Assert.That(DungeonRoomRules.WithOtherRookie(1, new Dictionary<long, bool> { [1] = false }), Is.False);
            Assert.That(DungeonRoomRules.WithOtherRookie(1, new Dictionary<long, bool>()), Is.False);
        });
    }

    // The client takes a room's cooldown type from cooldownValue alone: 0 is the next day, else a weekday.
    [TestCase(0, DungeonCooldownType.nextDay)]
    [TestCase(6, DungeonCooldownType.dayOfWeeks)]
    [TestCase(1, DungeonCooldownType.dayOfWeeks)]
    public void CooldownType_FromValue(int value, DungeonCooldownType expected) {
        Assert.That(DungeonRoomRules.CooldownType(value), Is.EqualTo(expected));
    }

    // 2026-11-11 is a Wednesday; cooldownValue 6 is Friday (Sunday 1), the next one a week on when it is today.
    [TestCase("2026-11-11 15:30", DungeonCooldownType.dayOfWeeks, 6, "2026-11-13 00:00")]
    [TestCase("2026-11-13 10:00", DungeonCooldownType.dayOfWeeks, 6, "2026-11-20 00:00")]
    [TestCase("2026-11-14 00:00", DungeonCooldownType.dayOfWeeks, 6, "2026-11-20 00:00")]
    [TestCase("2026-11-11 15:30", DungeonCooldownType.dayOfWeeks, 1, "2026-11-15 00:00")]
    [TestCase("2026-11-11 23:59", DungeonCooldownType.nextDay, 0, "2026-11-12 00:00")]
    [TestCase("2026-11-11 15:30", DungeonCooldownType.none, 6, "2026-11-11 15:30")]
    [TestCase("2026-11-11 15:30", DungeonCooldownType.dayOfWeeks, 8, "2026-11-11 15:30")]
    public void CooldownEnd_FollowsTheClient(string clear, DungeonCooldownType type, int value, string expected) {
        Assert.That(DungeonRoomRules.CooldownEnd(type, value, DateTime.Parse(clear)), Is.EqualTo(DateTime.Parse(expected)));
    }

    // The Colosseum row (cooldownValue 6, no cooldownType in the played client's table): a run that cleared one
    // round keeps a reward count of 1 until Friday, so receivedRewardDungeonID holds and Nagi offers to continue.
    [Test]
    public void ColosseumRound_CountsUntilTheWeekEnds() {
        DateTime clear = DateTime.Parse("2026-11-11 15:30");
        var record = new DungeonRecord(24096001) {
            UnionClears = 1,
            UnionCooldownTimestamp = Maple2.Database.Extensions.DateTimeExtensions.ToEpochSeconds(
                DungeonRoomRules.CooldownEnd(DungeonRoomRules.CooldownType(6), 6, clear)),
        };
        long now = Maple2.Database.Extensions.DateTimeExtensions.ToEpochSeconds(clear.AddHours(1));
        long after = Maple2.Database.Extensions.DateTimeExtensions.ToEpochSeconds(DateTime.Parse("2026-11-13 00:01"));
        Assert.Multiple(() => {
            Assert.That(Maple2.Server.Game.Manager.DungeonManager.RewardCount(record, now), Is.EqualTo(1));
            Assert.That(Maple2.Server.Game.Manager.DungeonManager.RewardCondition(1, 0, complete: false), Is.True);
            Assert.That(Maple2.Server.Game.Manager.DungeonManager.RewardCount(record, after), Is.EqualTo(0));
        });
    }
}
