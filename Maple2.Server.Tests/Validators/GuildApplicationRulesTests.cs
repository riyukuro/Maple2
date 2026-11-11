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
        Assert.That(GuildRules.CheckUpgradeSkill(true, 1000, current, next, 3, 500), Is.EqualTo(GuildError.none));
    }

    [Test]
    public void UseSkill_Rules() {
        var row = new GuildTable.Buff(Id: 1, Level: 1, RequireLevel: 3, Cost: 500, UpgradeCost: 0, Duration: 60);

        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.none));
        Assert.That(GuildRules.CheckUseSkill(false, 1, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_no_master));
        Assert.That(GuildRules.CheckUseSkill(true, 1, null, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_unknown), "no row of the level");
        Assert.That(GuildRules.CheckUseSkill(true, 10001, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_unknown), "personal skill");
        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 2, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        Assert.That(GuildRules.CheckUseSkill(true, 1, row, 3, 499, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        Assert.That(GuildRules.CheckUseSkill(true, 1, row with { Cost = -1 }, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
        // Paid from the event fund, which the server does not keep.
        Assert.That(GuildRules.CheckUseSkill(true, 1001, row, 3, 500, 0, 1000), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_fund));
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
    public void Donate_CountOfAnEarlierDayDoesNotCarryOver() {
        Assert.That(GuildRules.DonationsToday(4, 999, 1000), Is.EqualTo(0));
        Assert.That(GuildRules.DonationsToday(4, 1000, 1000), Is.EqualTo(4));
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
}
