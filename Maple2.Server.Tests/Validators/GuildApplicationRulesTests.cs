using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class GuildApplicationRulesTests {
    [TestCase(0, true)]
    [TestCase(9, true)]
    [TestCase(10, false)]
    [TestCase(11, false)]
    public void AtMostTenOpenRequests(int open, bool expected) {
        Assert.That(GuildRules.CanApply(open), Is.EqualTo(expected));
    }

    [Test]
    public void CancelIdIsTheGuildFirstThenTheRequest() {
        GuildApplicationRow[] own = [
            new GuildApplicationRow(Id: 7, GuildId: 3, ApplicantId: 1, CreationTime: 0),
            new GuildApplicationRow(Id: 3, GuildId: 9, ApplicantId: 1, CreationTime: 0),
        ];

        Assert.That(GuildRules.FindOwnApplication(own, 9)?.Id, Is.EqualTo(3));
        Assert.That(GuildRules.FindOwnApplication(own, 7)?.Id, Is.EqualTo(7));
        // 3 is both a guild and a request of this player: the guild wins, as the client names the id.
        Assert.That(GuildRules.FindOwnApplication(own, 3)?.Id, Is.EqualTo(7));
        Assert.That(GuildRules.FindOwnApplication(own, 5), Is.Null);
        Assert.That(GuildRules.FindOwnApplication([], 3), Is.Null);
    }

    // Guild 84: the accept page's script sends the request id ("requestSn"). Only this guild's request ids
    // match; a character id ("charSn") does not, so it cannot name another applicant's request.
    [Test]
    public void RespondIdIsTheRequestId() {
        GuildApplicationRow[] guildRows = [
            new GuildApplicationRow(Id: 1, GuildId: 3, ApplicantId: 2000, CreationTime: 0),
            new GuildApplicationRow(Id: 2, GuildId: 3, ApplicantId: 1, CreationTime: 0),
        ];

        Assert.That(GuildRules.FindGuildApplication(guildRows, 2)?.ApplicantId, Is.EqualTo(1));
        Assert.That(GuildRules.FindGuildApplication(guildRows, 2000), Is.Null);
        // 1 is both a request id and a character id: only the request matches.
        Assert.That(GuildRules.FindGuildApplication(guildRows, 1)?.ApplicantId, Is.EqualTo(2000));
        Assert.That(GuildRules.FindGuildApplication(guildRows, 5), Is.Null);
        Assert.That(GuildRules.FindGuildApplication([], 1), Is.Null);
    }

    private static GuildApplicationRow[] Rows(int count, long applicantId = 1) =>
        Enumerable.Range(1, count).Select(i => new GuildApplicationRow(Id: 100 + i, GuildId: i, ApplicantId: applicantId, CreationTime: 0)).ToArray();

    [Test]
    public void Apply_RulesOfTheWorldServer() {
        Assert.That(GuildRules.CheckApply(true, 0, [], 50), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckApply(false, 0, [], 50), Is.EqualTo(GuildError.s_guild_err_null_guild), "a guild not listed takes no request");
        Assert.That(GuildRules.CheckApply(true, 8, [], 50), Is.EqualTo(GuildError.s_guild_err_has_guild));
        Assert.That(GuildRules.CheckApply(true, 0, Rows(3), 2), Is.EqualTo(GuildError.s_guild_search_last_request), "one request per guild");
        Assert.That(GuildRules.CheckApply(true, 0, Rows(9), 50), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckApply(true, 0, Rows(10), 50), Is.EqualTo(GuildError.s_guild_search_max_join_request));
    }

    [Test]
    public void Respond_ForgedAnswersAreRefused() {
        var row = new GuildApplicationRow(Id: 7, GuildId: 3, ApplicantId: 40, CreationTime: 0);

        Assert.That(GuildRules.CheckRespond(true, true, 3, 40, row, true, 0), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckRespond(true, true, 3, 40, row, false, 0), Is.EqualTo(GuildError.none));
        // The sender is not in the guild named in the call.
        Assert.That(GuildRules.CheckRespond(false, true, 3, 40, row, true, 0), Is.EqualTo(GuildError.s_guild_err_null_member));
        // A member without the invite right.
        Assert.That(GuildRules.CheckRespond(true, false, 3, 40, row, true, 0), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(GuildRules.CheckRespond(true, false, 3, 40, row, false, 0), Is.EqualTo(GuildError.s_guild_err_no_authority));
        // The applicant never asked, or asked another guild, or the row is of another applicant.
        Assert.That(GuildRules.CheckRespond(true, true, 3, 40, null, true, 0), Is.EqualTo(GuildError.s_guild_search_null_join_guild_request));
        Assert.That(GuildRules.CheckRespond(true, true, 4, 40, row, true, 0), Is.EqualTo(GuildError.s_guild_search_null_join_guild_request));
        Assert.That(GuildRules.CheckRespond(true, true, 3, 41, row, true, 0), Is.EqualTo(GuildError.s_guild_search_null_join_guild_request));
        // An applicant who joined a guild since cannot be accepted; a refusal still goes through.
        Assert.That(GuildRules.CheckRespond(true, true, 3, 40, row, true, 9), Is.EqualTo(GuildError.s_guild_err_has_guild));
        Assert.That(GuildRules.CheckRespond(true, true, 3, 40, row, false, 9), Is.EqualTo(GuildError.none));
    }

    private static GuildTable.Buff SkillRow(short requireLevel, int upgradeCost) =>
        new(Id: 90000001, Level: 1, RequireLevel: requireLevel, Cost: 100, UpgradeCost: upgradeCost, Duration: 60);

    private static GuildTable.House HouseRow(int requireLevel, int upgradeCost, int reThemeCost) =>
        new(MapId: 1, RequireLevel: requireLevel, UpgradeCost: upgradeCost, ReThemeCost: reThemeCost, Facilities: []);

    [Test]
    public void UpgradeHouse_Rules() {
        GuildTable.House next = HouseRow(6, 500, 100);

        Assert.That(GuildRules.CheckUpgradeHouse(true, next, 6, 500), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUpgradeHouse(true, null, 6, 500), Is.EqualTo(GuildError.s_guild_err_unknown), "no such level or theme");
        Assert.That(GuildRules.CheckUpgradeHouse(false, next, 6, 500), Is.EqualTo(GuildError.s_guild_err_no_master));
        Assert.That(GuildRules.CheckUpgradeHouse(true, next, 5, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        Assert.That(GuildRules.CheckUpgradeHouse(true, next, 6, 499), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUpgradeHouse(true, HouseRow(6, -1, 0), 6, 499), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
    }

    [Test]
    public void ChangeHouseTheme_Rules() {
        GuildTable.House house = HouseRow(6, 500, 100);

        Assert.That(GuildRules.CheckChangeHouseTheme(true, house, 1, 2, 100), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckChangeHouseTheme(true, null, 1, 2, 100), Is.EqualTo(GuildError.s_guild_err_unknown), "no such theme at this level");
        Assert.That(GuildRules.CheckChangeHouseTheme(false, house, 1, 2, 100), Is.EqualTo(GuildError.s_guild_err_no_master));
        Assert.That(GuildRules.CheckChangeHouseTheme(true, house, 2, 2, 100), Is.EqualTo(GuildError.s_guild_err_unknown), "same theme");
        Assert.That(GuildRules.CheckChangeHouseTheme(true, house, 1, 2, 99), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckChangeHouseTheme(true, HouseRow(6, 0, -1), 1, 2, 99), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
    }

    [Test]
    public void UpgradeNpc_Rules() {
        var next = new GuildTable.Npc(GuildNpcType.Equip, Level: 2, RequireGuildLevel: 3, RequireHouseLevel: 2, UpgradeCost: 500);

        Assert.That(GuildRules.CheckUpgradeNpc(true, next, 3, 2, 500), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUpgradeNpc(true, null, 3, 2, 500), Is.EqualTo(GuildError.s_guild_err_unknown), "no next level");
        Assert.That(GuildRules.CheckUpgradeNpc(false, null, 3, 2, 500), Is.EqualTo(GuildError.s_guild_err_unknown), "the level is checked before the master");
        Assert.That(GuildRules.CheckUpgradeNpc(false, next, 3, 2, 500), Is.EqualTo(GuildError.s_guild_err_no_master));
        Assert.That(GuildRules.CheckUpgradeNpc(true, next, 2, 2, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        Assert.That(GuildRules.CheckUpgradeNpc(true, next, 3, 1, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level), "house level");
        Assert.That(GuildRules.CheckUpgradeNpc(true, next, 3, 2, 499), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUpgradeNpc(true, next with { UpgradeCost = -1 }, 3, 2, 499), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
    }

    [Test]
    public void UpgradeSkill_Rules() {
        GuildTable.Buff current = SkillRow(1, 0);
        GuildTable.Buff next = SkillRow(3, 500);

        Assert.That(GuildRules.CheckUpgradeSkill(true, 1, current, next, 3, 500), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 10001, current, next, 3, 500), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1, current, null, 3, 500), Is.EqualTo(GuildError.s_guild_err_unknown), "no next level");
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1, null, next, 3, 500), Is.EqualTo(GuildError.s_guild_err_unknown), "no row of the current level");
        Assert.That(GuildRules.CheckUpgradeSkill(false, 1, current, next, 3, 500), Is.EqualTo(GuildError.s_guild_err_no_master));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1, current, next, 2, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1, current, next, 3, 499), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1, current, SkillRow(3, -1), 3, 499), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        // Paid from the event fund, which the server does not keep.
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1001, current, next, 3, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 10000, current, next, 3, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1001, current, SkillRow(3, 0), 3, 500), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund), "also at cost 0");
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1000, current, next, 3, 500), Is.EqualTo(GuildError.none));
    }

    [Test]
    public void UseSkill_Rules() {
        var row = new GuildTable.Buff(Id: 1, Level: 1, RequireLevel: 3, Cost: 500, UpgradeCost: 0, Duration: 60);

        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUseSkill(false, 1, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_no_master));
        Assert.That(GuildRules.CheckUseSkill(true, 1, null, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_unknown), "no row of the level");
        Assert.That(GuildRules.CheckUseSkill(true, 10001, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_unknown), "personal skill");
        Assert.That(GuildRules.CheckUseSkill(true, 1, row with { Duration = 0 }, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_unknown), "no duration");
        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 2, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 3, 499, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUseSkill(true, 1, row with { Cost = -1 }, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        // Paid from the event fund, which the server does not keep.
        Assert.That(GuildRules.CheckUseSkill(true, 1001, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUseSkill(true, 1001, row with { Cost = 0 }, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund), "also at cost 0");
        // 60 minutes from the time of use.
        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 3, 500, 1000, 4599), Is.EqualTo(GuildError.s_guild_err_cannot_use_skill));
        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 3, 500, 1000, 4600), Is.EqualTo(GuildError.none));
        // A cooltime of 90 minutes holds the next use back after the 60 minutes of the effect.
        GuildTable.Buff cooling = row with { Cooldown = 90 };
        Assert.That(GuildRules.CheckUseSkill(true, 1, cooling, 3, 500, 1000, 4600), Is.EqualTo(GuildError.s_guild_err_cannot_use_skill));
        Assert.That(GuildRules.CheckUseSkill(true, 1, cooling, 3, 500, 1000, 6399), Is.EqualTo(GuildError.s_guild_err_cannot_use_skill));
        Assert.That(GuildRules.CheckUseSkill(true, 1, cooling, 3, 500, 1000, 6400), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUseSkill(true, 1, cooling, 3, 500, 0, 1700000000), Is.EqualTo(GuildError.none), "never used");
    }

    [Test]
    public void UsePersonalSkill_Rules() {
        var row = new GuildTable.Buff(Id: 1, Level: 1, RequireLevel: 3, Cost: 500, UpgradeCost: 0, Duration: 60);

        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row, 3, 500, false), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, null, 3, 500, false), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(GuildRules.CheckUsePersonalSkill(10000, row, 3, 500, false), Is.EqualTo(GuildError.s_guild_err_unknown), "a guild skill");
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row with { Duration = 0 }, 3, 500, false), Is.EqualTo(GuildError.s_guild_err_unknown), "no duration");
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row with { Id = 0 }, 3, 500, false), Is.EqualTo(GuildError.s_guild_err_unknown), "no effect");
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row, 2, 500, false), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row, 3, 499, false), Is.EqualTo(GuildError.s_guild_err_no_money));
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row with { Cost = -1 }, 3, 500, false), Is.EqualTo(GuildError.s_guild_err_no_money));
        Assert.That(GuildRules.CheckUsePersonalSkill(10001, row, 3, 500, true), Is.EqualTo(GuildError.s_guild_err_cannot_use_skill));
    }

    // What a member who logs in while the skill runs gets: the client's remaining time.
    [TestCase(0, 1000, 0, Description = "never used")]
    [TestCase(1000, 1000, 3600)]
    [TestCase(1000, 2500, 2100)]
    [TestCase(1000, 4600, 0)]
    [TestCase(1000, 9000, 0)]
    public void UseSkill_RemainingSeconds(long useTime, long now, long expected) {
        var row = new GuildTable.Buff(Id: 1, Level: 1, RequireLevel: 3, Cost: 500, UpgradeCost: 0, Duration: 60);
        Assert.That(GuildRules.SkillRemainingSeconds(row, useTime, now), Is.EqualTo(expected));
    }

    [Test]
    public void Mail_GoesToEveryMemberButTheSender() {
        Assert.That(GuildRules.MailReceivers([5, 3, 9], 3), Is.EqualTo(new long[] { 5, 9 }));
        Assert.That(GuildRules.MailReceivers([3], 3), Is.Empty);
    }

    [TestCase(1000, 0, true, Description = "first mail")]
    [TestCase(1000, 1000, false)]
    [TestCase(1059, 1000, false)]
    [TestCase(1060, 1000, true)]
    [TestCase(900, 1000, true, Description = "the clock went back")]
    public void Mail_OnePerSenderInSixtySeconds(long now, long last, bool expected) {
        Assert.That(GuildRules.CanSendMail(now, last), Is.EqualTo(expected));
    }

    [Test]
    public void Mail_TurnIsRefusedInsideTheLimit() {
        var lastMail = new System.Collections.Generic.Dictionary<long, long>();

        Assert.That(GuildRules.TakeMailTurn(lastMail, 7, 1000), Is.True);
        Assert.That(GuildRules.TakeMailTurn(lastMail, 7, 1059), Is.False);
        Assert.That(lastMail[7], Is.EqualTo(1000), "a refused turn does not move the time");
        Assert.That(GuildRules.TakeMailTurn(lastMail, 8, 1059), Is.True, "another sender has an own limit");
        Assert.That(GuildRules.TakeMailTurn(lastMail, 7, 1060), Is.True);
        Assert.That(lastMail[7], Is.EqualTo(1060));
    }

    [Test]
    public void Mail_OldTurnsAreRemovedOnEachInsert() {
        var lastMail = new System.Collections.Generic.Dictionary<long, long>();
        for (long id = 1; id <= 50; id++) {
            Assert.That(GuildRules.TakeMailTurn(lastMail, id, 1000 + id), Is.True);
        }
        Assert.That(lastMail, Has.Count.EqualTo(50));

        // At 1080 the senders 1 to 20 (1001 to 1020) are free again and go; 21 to 50 stay, 99 is added.
        Assert.That(GuildRules.TakeMailTurn(lastMail, 99, 1080), Is.True);
        Assert.That(lastMail, Has.Count.EqualTo(31));
        Assert.That(lastMail.ContainsKey(20), Is.False);
        Assert.That(lastMail.ContainsKey(21), Is.True);

        // Much later every other entry goes.
        Assert.That(GuildRules.TakeMailTurn(lastMail, 99, 1200), Is.True);
        Assert.That(lastMail, Has.Count.EqualTo(1));
    }

    [Test]
    public void Donate_CountOfAnEarlierDayDoesNotCarryOver() {
        Assert.That(GuildRules.DonationsToday(4, 999, 1000), Is.EqualTo(0));
        Assert.That(GuildRules.DonationsToday(4, 1000, 1000), Is.EqualTo(4));
    }

    // The day starts at midnight in the server's zone, the zone the client's own day test runs in.
    [Test]
    public void DayStart_IsLocalMidnight() {
        TimeZoneInfo kst = TimeZoneInfo.CreateCustomTimeZone("t9", TimeSpan.FromHours(9), "t9", "t9");
        long Kst(int day, int hour) => new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeSeconds();

        Assert.Multiple(() => {
            Assert.That(GuildRules.DayStart(Kst(12, 1), kst), Is.EqualTo(Kst(12, 0)));
            Assert.That(GuildRules.DayStart(Kst(12, 23), kst), Is.EqualTo(Kst(12, 0)));
            Assert.That(GuildRules.DayStart(Kst(12, 0), kst), Is.EqualTo(Kst(12, 0)));
            Assert.That(GuildRules.DayStart(Kst(12, 1), TimeZoneInfo.Utc),
                Is.EqualTo(new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
        });
    }

    [Test]
    public void CheckIn_DayChangesAtLocalMidnight() {
        TimeZoneInfo kst = TimeZoneInfo.CreateCustomTimeZone("t9", TimeSpan.FromHours(9), "t9", "t9");
        long Kst(int day, int hour) => new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeSeconds();
        long joined = Kst(1, 12);

        Assert.Multiple(() => {
            // Checked in at 23:00, again at 01:00 the next day: a new day for the client; the UTC day had not turned.
            Assert.That(GuildRules.CanCheckIn(joined, Kst(11, 23), GuildRules.DayStart(Kst(12, 1), kst)), Is.True);
            Assert.That(GuildRules.CanCheckIn(joined, Kst(11, 23), GuildRules.DayStart(Kst(12, 1), TimeZoneInfo.Utc)), Is.False);
            // Checked in at 01:00, again at 10:00: the same local day, though the UTC day turned at 09:00.
            Assert.That(GuildRules.CanCheckIn(joined, Kst(12, 1), GuildRules.DayStart(Kst(12, 10), kst)), Is.False);
            Assert.That(GuildRules.CanCheckIn(joined, Kst(12, 1), GuildRules.DayStart(Kst(12, 10), TimeZoneInfo.Utc)), Is.True);
            // Joined at 23:00, checking in at 01:00 the next day.
            Assert.That(GuildRules.CanCheckIn(Kst(11, 23), 0, GuildRules.DayStart(Kst(12, 1), kst)), Is.True);
        });
    }

    [Test]
    public void Donate_CountResetsAtLocalMidnight() {
        TimeZoneInfo kst = TimeZoneInfo.CreateCustomTimeZone("t9", TimeSpan.FromHours(9), "t9", "t9");
        long Kst(int day, int hour) => new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeSeconds();

        Assert.Multiple(() => {
            Assert.That(GuildRules.DonationsToday(3, Kst(11, 23), GuildRules.DayStart(Kst(12, 1), kst)), Is.EqualTo(0));
            Assert.That(GuildRules.DonationsToday(3, Kst(12, 1), GuildRules.DayStart(Kst(12, 10), kst)), Is.EqualTo(3));
        });
    }

    // joinTime, checkinTime, dayStart
    [TestCase(500, 0, 1000, true, Description = "joined before today, never checked in")]
    [TestCase(500, 999, 1000, true, Description = "checked in yesterday")]
    [TestCase(1000, 0, 1000, false, Description = "joined today")]
    [TestCase(1500, 0, 1000, false)]
    [TestCase(500, 1000, 1000, false, Description = "checked in today")]
    [TestCase(500, 1200, 1000, false)]
    public void CheckIn_Rules(long joinTime, long checkinTime, long dayStart, bool expected) {
        Assert.That(GuildRules.CanCheckIn(joinTime, checkinTime, dayStart), Is.EqualTo(expected));
    }

    // count, joinTime, dayStart, donationsToday, donateMax, funds, addFunds, fundMax
    [TestCase(1, 500, 1000, 0, 3, 0, 10000, 50000, true)]
    [TestCase(3, 500, 1000, 0, 3, 0, 30000, 50000, true)]
    [TestCase(0, 500, 1000, 0, 3, 0, 0, 50000, false, Description = "a count under 1")]
    [TestCase(-1, 500, 1000, 0, 3, 0, 10000, 50000, false)]
    [TestCase(1, 1000, 1000, 0, 3, 0, 10000, 50000, false, Description = "joined today")]
    [TestCase(2, 500, 1000, 2, 3, 0, 20000, 50000, false, Description = "over the day's limit")]
    [TestCase(1, 500, 1000, 2, 3, 0, 10000, 50000, true)]
    [TestCase(1, 500, 1000, 0, 3, 45000, 10000, 50000, false, Description = "over the fund maximum")]
    [TestCase(1, 500, 1000, 0, 3, 40000, 10000, 50000, true)]
    [TestCase(1, 500, 1000, 0, 3, 0, -5, 50000, false)]
    [TestCase(int.MaxValue, 500, 1000, 1, 3, 0, 10000, 50000, false)]
    public void Donate_Rules(int count, long joinTime, long dayStart, int today, int donateMax, long funds, long add, long fundMax, bool expected) {
        Assert.That(GuildRules.CanDonate(count, joinTime, dayStart, today, donateMax, funds, add, fundMax), Is.EqualTo(expected));
    }

    // has row, creation time, right, time of the last opening, now
    [TestCase(true, 0, true, 0, 86400, GuildError.none)]
    [TestCase(false, 0, true, 0, 86400, GuildError.s_guild_err_unknown)]
    [TestCase(true, 1, true, 0, 86400, GuildError.s_guild_err_unknown)] // a second short of a day old
    [TestCase(true, 0, false, 0, 86400, GuildError.s_guild_err_no_authority)]
    [TestCase(true, 0, true, 86371, 86400, GuildError.s_guild_err_unknown)] // opened 29 s ago: still open
    [TestCase(true, 0, true, 86370, 86400, GuildError.none)] // opened 30 s ago: over
    public void OpenEvent_Rules(bool hasRow, long creationTime, bool hasRight, long openTime, long now, GuildError expected) {
        Assert.That(GuildRules.CheckOpenEvent(hasRow, creationTime, hasRight, openTime, now), Is.EqualTo(expected));
    }

    [TestCase(0, 10, false)] // never opened
    [TestCase(100, 129, true)]
    [TestCase(100, 130, false)]
    public void EventActive_Rules(long openTime, long now, bool expected) {
        Assert.That(GuildRules.IsEventActive(openTime, now), Is.EqualTo(expected));
    }

    // event map, time of opening, now, current map
    [TestCase(500, 100, 110, 200, true)]
    [TestCase(0, 100, 110, 200, false)] // no row
    [TestCase(500, 0, 110, 200, false)] // never opened
    [TestCase(500, 100, 130, 200, false)] // over
    [TestCase(500, 100, 110, 500, false)] // in the map already
    public void EnterEvent_Rules(int eventMapId, long openTime, long now, int currentMapId, bool expected) {
        Assert.That(GuildRules.CanEnterEvent(eventMapId, openTime, now, currentMapId), Is.EqualTo(expected));
    }

    // PvP map, the effect is kept on entering a PvP map, the effect is removed in a PvP map
    [TestCase(false, false, false, true)]
    [TestCase(false, false, true, true)]
    [TestCase(true, true, false, true)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, true, false)]
    public void SkillEffectOnEntry_Rules(bool isPvpMap, bool keep, bool remove, bool expected) {
        Assert.That(GuildRules.GivesEffectOnEntry(isPvpMap, keep, remove), Is.EqualTo(expected));
    }

    [Test]
    public void SkillEffects_AreTheRowsOfTheGuildsLevels() {
        var table = new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> {
            [1] = new Dictionary<short, GuildTable.Buff> {
                [1] = new GuildTable.Buff(Id: 900, Level: 1, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 60),
                [2] = new GuildTable.Buff(Id: 900, Level: 2, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 60),
            },
            [10001] = new Dictionary<short, GuildTable.Buff> {
                [1] = new GuildTable.Buff(Id: 950, Level: 1, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 60),
            },
            [3] = new Dictionary<short, GuildTable.Buff> {
                [1] = new GuildTable.Buff(Id: 0, Level: 1, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 60),
            },
        };
        GuildBuff[] skills = [new GuildBuff(1, 2, 0), new GuildBuff(10001, 1, 0), new GuildBuff(3, 1, 0), new GuildBuff(4, 1, 0)];

        // Skill 3 has no effect and skill 4 no row: neither is listed.
        Assert.That(GuildRules.SkillEffects(skills, table), Is.EqualTo(new List<(int, short)> { (900, 2), (950, 1) }));
    }

    // Guild 91: the member pays the row's cost once, and nothing when the use is refused.
    [Test]
    public void PersonalSkillCharge_TakesTheCostOnlyOnSuccess() {
        var row = new GuildTable.Buff(Id: 950, Level: 1, RequireLevel: 3, Cost: 500, UpgradeCost: 0, Duration: 60);

        Assert.That(GuildRules.PersonalSkillCharge(10001, row, 3, 800, false, out long charge), Is.EqualTo(GuildError.none));
        Assert.That(charge, Is.EqualTo(500));
        Assert.That(800 - charge, Is.EqualTo(300), "the meso left");

        // The member has the effect: error 44, no meso.
        Assert.That(GuildRules.PersonalSkillCharge(10001, row, 3, 800, true, out charge), Is.EqualTo(GuildError.s_guild_err_cannot_use_skill));
        Assert.That((int) GuildError.s_guild_err_cannot_use_skill, Is.EqualTo(44));
        Assert.That(charge, Is.EqualTo(0));

        Assert.That(GuildRules.PersonalSkillCharge(10001, row, 3, 499, false, out charge), Is.EqualTo(GuildError.s_guild_err_no_money));
        Assert.That(charge, Is.EqualTo(0));
        Assert.That(GuildRules.PersonalSkillCharge(10001, null, 3, 800, false, out charge), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(charge, Is.EqualTo(0));
    }

    // LoadSkillBuffs gives what this lists: one effect per running guild skill, for the time left.
    [Test]
    public void RunningSkillEffects_OnePerSkill_NothingAfterTheEnd() {
        var table = new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> {
            [1] = new Dictionary<short, GuildTable.Buff> {
                [2] = new GuildTable.Buff(Id: 900, Level: 2, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 60),
            },
            [2] = new Dictionary<short, GuildTable.Buff> {
                [1] = new GuildTable.Buff(Id: 901, Level: 1, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 10),
            },
            [10001] = new Dictionary<short, GuildTable.Buff> {
                [1] = new GuildTable.Buff(Id: 950, Level: 1, RequireLevel: 1, Cost: 1, UpgradeCost: 0, Duration: 60),
            },
        };
        // Skill 1 used at 1000 (60 minutes), skill 2 at 1000 (10 minutes), skill 3 has no row, the personal
        // skill 10001 is not a guild effect, skill 1 at level 9 has no row.
        GuildBuff[] skills = [new GuildBuff(1, 2, 1000), new GuildBuff(2, 1, 1000), new GuildBuff(3, 1, 1000),
            new GuildBuff(10001, 1, 1000), new GuildBuff(1, 9, 1000)];

        Assert.That(GuildRules.RunningSkillEffects(skills, table, 1100),
            Is.EqualTo(new List<(int, short, long)> { (900, 2, 3500), (901, 1, 500) }), "one entry per running skill");
        Assert.That(GuildRules.RunningSkillEffects(skills, table, 1599),
            Is.EqualTo(new List<(int, short, long)> { (900, 2, 3001), (901, 1, 1) }));
        Assert.That(GuildRules.RunningSkillEffects(skills, table, 1600),
            Is.EqualTo(new List<(int, short, long)> { (900, 2, 3000) }), "skill 2 ended");
        Assert.That(GuildRules.RunningSkillEffects(skills, table, 4600), Is.Empty, "nothing after the end");
        Assert.That(GuildRules.RunningSkillEffects([new GuildBuff(1, 2, 0)], table, 100), Is.Empty, "never used");
    }

    // In the client's order: the master, a member receiver, a count within a stack and the stored count, OnlyToMe to
    // the master alone; then the server's need period (7 days here, joined at 0).
    [TestCase(true, true, 1, 1, false, false, 8, GuildError.none)]
    [TestCase(false, false, 1, 1, false, false, 8, GuildError.s_guild_err_no_master)]
    [TestCase(true, false, 1, 1, false, false, 8, GuildError.s_guild_err_null_member)]
    [TestCase(true, true, 0, 5, false, false, 8, GuildError.s_guild_err_unknown)]
    [TestCase(true, true, -1, 5, false, false, 8, GuildError.s_guild_err_unknown)]
    [TestCase(true, true, 2, 1, false, false, 8, GuildError.s_guild_err_unknown)]
    [TestCase(true, true, 11, 20, false, false, 8, GuildError.s_guild_err_unknown)]
    [TestCase(true, true, 1, 1, true, false, 8, GuildError.s_guild_err_unknown)]
    [TestCase(true, true, 1, 1, true, true, 8, GuildError.none)]
    [TestCase(true, true, 1, 1, false, false, 6, GuildError.s_guild_storage_not_enough_gift_need_period)]
    [TestCase(true, true, 1, 1, false, false, 7, GuildError.none)]
    public void StorageGift_Rules(bool isMaster, bool receiverIsMember, int count, int stored, bool onlyToMe, bool toSelf,
            int daysJoined, GuildError expected) {
        Assert.That(GuildRules.CheckStorageGift(isMaster, receiverIsMember, count, 10, stored, onlyToMe, toSelf, 0, 7,
            daysJoined * 86400L), Is.EqualTo(expected));
    }

    [TestCase(false, false, false, true)]
    [TestCase(true, false, false, false)]
    [TestCase(false, true, false, false)]
    [TestCase(false, false, true, false)]
    public void GuildFieldEntry_NeedsAMemberFreeToLeave(bool dead, bool inDungeon, bool inHome, bool expected) {
        Assert.That(GuildRules.CanGoToGuildField(dead, inDungeon, inHome), Is.EqualTo(expected));
    }
}
