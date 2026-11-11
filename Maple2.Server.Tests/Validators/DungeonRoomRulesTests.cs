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
}
