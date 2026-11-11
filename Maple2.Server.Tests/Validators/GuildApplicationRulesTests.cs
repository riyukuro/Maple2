using System.Linq;
using Maple2.Model.Error;
using Maple2.Model.Game;
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

    [Test]
    public void Mail_GoesToEveryMemberButTheSender() {
        Assert.That(GuildRules.MailReceivers([5, 3, 9], 3), Is.EqualTo(new long[] { 5, 9 }));
        Assert.That(GuildRules.MailReceivers([3], 3), Is.Empty);
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
