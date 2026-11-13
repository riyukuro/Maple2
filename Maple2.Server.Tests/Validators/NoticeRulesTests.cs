using System;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Validators;

public class NoticeRulesTests {
    [TestCase(NoticeRules.Banner, NoticeRules.On, true, false)]
    [TestCase(NoticeRules.Banner, NoticeRules.Off, false, false)]
    [TestCase(NoticeRules.Login, NoticeRules.On, false, true)]
    [TestCase(NoticeRules.Login, NoticeRules.Off, false, false)]
    public void OnAndOffSetTheirKindOnly(int kind, int action, bool banner, bool login) {
        Assert.That(NoticeRules.Apply(false, false, kind, action), Is.EqualTo((banner, login)));
        // The other kind keeps its value.
        (bool bannerAfter, bool loginAfter) = NoticeRules.Apply(true, true, kind, action);
        Assert.That(kind == NoticeRules.Banner ? loginAfter : bannerAfter, Is.True);
    }

    [TestCase(NoticeRules.Banner, NoticeRules.Show)]
    [TestCase(NoticeRules.Banner, NoticeRules.Status)]
    [TestCase(NoticeRules.Login, NoticeRules.Status)]
    [TestCase(NoticeRules.Login, NoticeRules.Show)] // no login show
    [TestCase(2, NoticeRules.On)]
    [TestCase(NoticeRules.Banner, 4)]
    [TestCase(-1, -1)]
    public void OtherRequestsChangeNothing(int kind, int action) {
        Assert.That(NoticeRules.Apply(true, false, kind, action), Is.EqualTo((true, false)));
        Assert.That(NoticeRules.Apply(false, true, kind, action), Is.EqualTo((false, true)));
    }

    [TestCase(NoticeRules.Banner, NoticeRules.Off, true)]
    [TestCase(NoticeRules.Banner, NoticeRules.On, true)]
    [TestCase(NoticeRules.Banner, NoticeRules.Show, true)]
    [TestCase(NoticeRules.Banner, NoticeRules.Status, true)]
    [TestCase(NoticeRules.Login, NoticeRules.On, true)]
    [TestCase(NoticeRules.Login, NoticeRules.Status, true)]
    [TestCase(NoticeRules.Login, NoticeRules.Show, false)]
    [TestCase(2, NoticeRules.Status, false)]
    [TestCase(NoticeRules.Banner, 4, false)]
    public void ValidRequests(int kind, int action, bool expected) {
        Assert.That(NoticeRules.ValidRequest(kind, action), Is.EqualTo(expected));
    }

    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void TheBannerComesOncePerSession(bool on, bool sent, bool expected) {
        Assert.That(NoticeRules.BannerAtEntry(on, sent), Is.EqualTo(expected));
    }

    [Test]
    public void TheLoginNoticeEventIsActiveForADay() {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Local);
        GameEvent gameEvent = NoticeRules.LoginNoticeEvent(now);
        Assert.Multiple(() => {
            Assert.That(gameEvent.Id, Is.EqualTo(NoticeRules.LoginNoticeEventId));
            Assert.That(gameEvent.Metadata.Type, Is.EqualTo(GameEventType.LoginNotice));
            Assert.That(gameEvent.Metadata.Data, Is.TypeOf<LoginNotice>());
            Assert.That(gameEvent.SentToClient, Is.True);
            Assert.That(gameEvent.IsActive(now), Is.True);
            Assert.That(gameEvent.IsActive(now.AddHours(23)), Is.True);
            Assert.That(gameEvent.IsActive(now.AddDays(2)), Is.False);
        });
    }

    // GameEventLoginNotice has no reader of its own: GameEvent::vfunc_4 reads the id after the name.
    [Test]
    public void TheLoginNoticeEventIsItsNameAndId() {
        var writer = new ByteWriter();
        writer.WriteClass<GameEvent>(NoticeRules.LoginNoticeEvent(DateTime.Now));
        Assert.That(Convert.ToHexString(writer.ToArray()),
            Is.EqualTo("0B00" + "4C006F00670069006E004E006F007400690063006500" + "01752B7D"));
    }
}
