using System.Collections.Generic;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// Guild and club names follow the client's own rule for the region (CheckGuildName and the club dialog), not the
// character name's ASCII pattern.
public class GuildClubNameRulesTests {
    // KR: Hangul syllables, single inner spaces and ' - _ . are taken; min 2, max 10 characters, Hangul counted once.
    [TestCase("\uBA54\uC774\uD50C", "KR", true)]
    [TestCase("Maple \uAE38\uB4DC", "KR", true)]
    [TestCase("a-b_c.d'e", "KR", true)]
    [TestCase("\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00", "KR", true)]
    [TestCase("\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00", "KR", false)]
    [TestCase("a", "KR", false)]
    [TestCase(" ab", "KR", false)]
    [TestCase("ab ", "KR", false)]
    [TestCase("a  b", "KR", false)]
    [TestCase("a@b", "KR", false)]
    [TestCase("\u00C4pfel", "KR", false)]
    [TestCase("\u00C4pfel", "NA", true)]
    [TestCase("Jo\u00E3o", "NA", true)]
    [TestCase("\uBA54\uC774\uD50C", "NA", false)]
    [TestCase("\u4E2D\u6587", "CN", true)]
    public void GuildName_FollowsTheRegionRule(string name, string locale, bool expected) {
        Assert.That(GuildRules.IsGuildName(name, 2, 10, locale), Is.EqualTo(expected));
    }

    // Rank names (CheckGuildGradeName): the guild name's characters, 1 to 7 characters, 15 on NA.
    [TestCase("a", "KR", true)]
    [TestCase("\uAE38\uB4DC \uC7A5", "KR", true)]
    [TestCase("Elder-1", "KR", true)]
    [TestCase("Elder-12", "KR", false)]
    [TestCase("Elder-12", "NA", true)]
    [TestCase("Senior Officer1", "NA", true)]
    [TestCase("Senior Officer12", "NA", false)]
    [TestCase("", "KR", false)]
    [TestCase(" a", "KR", false)]
    [TestCase("a@b", "KR", false)]
    public void RankName_FollowsTheClientRule(string name, string locale, bool expected) {
        Assert.That(GuildRules.IsRankName(name, locale), Is.EqualTo(expected));
    }

    // A new guild's ranks carry the client's default names of the region, which the rank name rule takes.
    [TestCase("KR")]
    [TestCase("NA")]
    [TestCase("CN")]
    public void DefaultRankNames_PassTheRankNameRule(string locale) {
        IReadOnlyList<string> names = GuildRules.DefaultRankNames(locale);
        Assert.That(names, Has.Count.EqualTo(6));
        Assert.That(names, Has.All.Matches<string>(name => GuildRules.IsRankName(name, locale)));
    }

    [Test]
    public void DefaultRankNames_AreTheClientStrings() {
        Assert.That(GuildRules.DefaultRankNames("KR")[1], Is.EqualTo("\uBD80\uAE38\uB4DC\uC7A5"));
        Assert.That(GuildRules.DefaultRankNames("NA")[4], Is.EqualTo("Guild Member 2"));
    }

    [TestCase("\uBA54\uC774\uD50C\uD074\uB7FD", true)]
    [TestCase("Club_1.x", true)]
    [TestCase("\u00C4pfel", true)]
    [TestCase("my club", false)]
    [TestCase("", false)]
    [TestCase("a\u0001b", false)]
    public void ClubName_TakesAnyLetterButNoSpace(string name, bool expected) {
        Assert.That(NameValidator.ValidClubName(name), Is.EqualTo(expected));
    }
}
