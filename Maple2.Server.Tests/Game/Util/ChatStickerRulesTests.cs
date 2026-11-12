using System.Collections.Generic;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class ChatStickerRulesTests {
    private const long Now = 1700000000;

    [Test]
    public void Usable_HeldGroupInTime() {
        var held = new Dictionary<int, long> { [1] = ChatStickerRules.Permanent, [2] = Now + 10, [3] = Now };
        Assert.That(ChatStickerRules.Usable(held, 1, Now), Is.True);
        Assert.That(ChatStickerRules.Usable(held, 2, Now), Is.True);
        Assert.That(ChatStickerRules.Usable(held, 3, Now), Is.True);    // the client's test is now <= expiry
    }

    [Test]
    public void Usable_UnknownSticker_IsRefused() {
        var held = new Dictionary<int, long> { [1] = ChatStickerRules.Permanent };
        Assert.That(ChatStickerRules.Usable(held, null, Now), Is.False);
    }

    [Test]
    public void Usable_GroupNotHeld_IsRefused() {
        var held = new Dictionary<int, long> { [1] = ChatStickerRules.Permanent };
        Assert.That(ChatStickerRules.Usable(held, 2, Now), Is.False);
    }

    [Test]
    public void Usable_GroupRunOut_IsRefused() {
        var held = new Dictionary<int, long> { [2] = Now - 1 };
        Assert.That(ChatStickerRules.Usable(held, 2, Now), Is.False);
    }

    [Test]
    public void NewExpiry_NewGroup() {
        Assert.That(ChatStickerRules.NewExpiry(null, 3600, Now), Is.EqualTo(Now + 3600));
        Assert.That(ChatStickerRules.NewExpiry(null, 0, Now), Is.EqualTo(ChatStickerRules.Permanent));
    }

    [Test]
    public void NewExpiry_AddsToTheTimeLeft() {
        Assert.That(ChatStickerRules.NewExpiry(Now + 100, 3600, Now), Is.EqualTo(Now + 3700));
        Assert.That(ChatStickerRules.NewExpiry(Now + 100, 0, Now), Is.EqualTo(ChatStickerRules.Permanent));
    }

    [Test]
    public void NewExpiry_OfAGroupRunOut_StartsFromNow() {
        Assert.That(ChatStickerRules.NewExpiry(Now - 100000, 3600, Now), Is.EqualTo(Now + 3600));
    }

    [Test]
    public void NewExpiry_PermanentGroup_IsRefused() {
        Assert.That(ChatStickerRules.NewExpiry(ChatStickerRules.Permanent, 3600, Now), Is.Null);
        Assert.That(ChatStickerRules.NewExpiry(ChatStickerRules.Permanent, 0, Now), Is.Null);
    }

    [Test]
    public void NewExpiry_NegativeTime_IsRefused() {
        Assert.That(ChatStickerRules.NewExpiry(null, -5, Now), Is.Null);
    }
}
