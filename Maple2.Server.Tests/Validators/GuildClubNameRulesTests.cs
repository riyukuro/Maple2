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
