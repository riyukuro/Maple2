using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Util;

// The chat spam restriction with the KR constants table values (milliseconds).
public class ChatRestrictionTests {
    private static readonly ConstantsTable Kr = new(
        FastChat_CheckTime: 2000, FastChat_CheckCount: 5,
        SameChat_CheckTime: 3000, SameChat_CheckCount: 5,
        SameChat_RestrictTime: 10000, FastChat_RestrictTime: 30000,
        RestrictChat_AddRestrictTime: 10000, AccumWarning_AddRestrictTime: 60000,
        RestrictWarning_ReleaseTime: 10000, MaxChatLength: 50);

    private ChatRestriction restriction = null!;

    [SetUp]
    public void SetUp() {
        restriction = new ChatRestriction();
    }

    // Four lines at start, start + 100, ...; the fifth is the result.
    private ChatRestriction.Result Burst(long start, bool same, ConstantsTable? constants = null) {
        for (int i = 0; i < 4; i++) {
            Assert.That(restriction.Check(constants ?? Kr, same ? "hi" : $"line {i}", start + i * 100).Allowed, Is.True, $"line {i}");
        }
        return restriction.Check(constants ?? Kr, same ? "hi" : "line 4", start + 400);
    }

    [Test]
    public void FifthEqualLine_IsSameChat() {
        Assert.That(Burst(0, same: true), Is.EqualTo(new ChatRestriction.Result(false, StringCode.s_chat_restrict_samechat, 10)));
    }

    [Test]
    public void FifthLineWithinTheFastWindow_IsFastChat() {
        Assert.That(Burst(0, same: false), Is.EqualTo(new ChatRestriction.Result(false, StringCode.s_chat_restrict_fastchat, 30)));
    }

    // Lines slower than the windows are never restricted.
    [Test]
    public void SlowLines_AreAllowed() {
        for (int i = 0; i < 20; i++) {
            Assert.That(restriction.Check(Kr, "hi", i * 1000L).Allowed, Is.True, $"line {i}");
        }
    }

    // A line while restricted adds RestrictChat_AddRestrictTime; the notice has the seconds left.
    [Test]
    public void LineWhileRestricted_AddsTime() {
        Burst(0, same: true);

        Assert.That(restriction.Check(Kr, "x", 1400), Is.EqualTo(new ChatRestriction.Result(false, StringCode.s_chat_restrict_addtime, 19)));
        Assert.That(restriction.Check(Kr, "x", 20399).Allowed, Is.False);
        Assert.That(restriction.Check(Kr, "x", 30400).Allowed, Is.True);
    }

    // A restriction within RestrictWarning_ReleaseTime after the last one ended is the repeated warning; later it
    // is a plain restriction again.
    [Test]
    public void RestrictionSoonAfterTheLast_IsTheAccumWarning() {
        Burst(0, same: true);

        Assert.That(Burst(11000, same: true), Is.EqualTo(new ChatRestriction.Result(false, StringCode.s_chat_restrict_accumwarning, 60)));
        // The line at 71399 adds RestrictChat_AddRestrictTime: the restriction ends at 81400.
        Assert.That(restriction.Check(Kr, "x", 71399).Allowed, Is.False);
        Assert.That(Burst(90999, same: true).Notice, Is.EqualTo(StringCode.s_chat_restrict_accumwarning));
        Assert.That(Burst(200000, same: true), Is.EqualTo(new ChatRestriction.Result(false, StringCode.s_chat_restrict_samechat, 10)));
    }

    // A count or window of 0 turns its check off, and the test table (all 0) restricts nothing.
    [Test]
    public void ZeroConstants_TurnTheChecksOff() {
        Assert.That(Burst(0, same: true, Kr with { SameChat_CheckCount = 0 }).Notice, Is.EqualTo(StringCode.s_chat_restrict_fastchat));
        restriction = new ChatRestriction();
        Assert.That(Burst(0, same: false, Kr with { FastChat_CheckTime = 0 }).Allowed, Is.True);
        restriction = new ChatRestriction();
        Assert.That(Burst(0, same: true, Kr with { SameChat_RestrictTime = 0 }).Allowed, Is.True);
        restriction = new ChatRestriction();
        var off = new ConstantsTable();
        for (int i = 0; i < 20; i++) {
            Assert.That(restriction.Check(off, "hi", i).Allowed, Is.True);
        }
    }

    [Test]
    public void Covers_TheTypesOfTheRestrictTexts() {
        Assert.That(ChatRestriction.Covers(ChatType.Normal), Is.True);
        Assert.That(ChatRestriction.Covers(ChatType.WhisperTo), Is.True);
        Assert.That(ChatRestriction.Covers(ChatType.World), Is.True);
        Assert.That(ChatRestriction.Covers(ChatType.Channel), Is.True);
        Assert.That(ChatRestriction.Covers(ChatType.Party), Is.False);
        Assert.That(ChatRestriction.Covers(ChatType.Guild), Is.False);
        Assert.That(ChatRestriction.Covers(ChatType.Super), Is.False);
    }

    // Item links are html: tags and entities are not what the player typed.
    [Test]
    public void VisibleLength_SkipsTagsAndEntities() {
        Assert.That(ChatRestriction.VisibleLength("abc"), Is.EqualTo(3));
        Assert.That(ChatRestriction.VisibleLength("a <A HREF=\"event:item,1\">[sword]</A> b"), Is.EqualTo(11));
        Assert.That(ChatRestriction.VisibleLength("a&amp;b&lt;"), Is.EqualTo(4));
        Assert.That(ChatRestriction.VisibleLength("\uAC00\uB098"), Is.EqualTo(2));
    }

    [Test]
    public void TooLong_PastMaxChatLength() {
        Assert.That(ChatRestriction.TooLong(Kr, new string('a', 50)), Is.False);
        Assert.That(ChatRestriction.TooLong(Kr, new string('a', 51)), Is.True);
        Assert.That(ChatRestriction.TooLong(Kr, "<b>" + new string('a', 50) + "</b>"), Is.False);
        Assert.That(ChatRestriction.TooLong(new ConstantsTable(), new string('a', 500)), Is.False);
    }
}
