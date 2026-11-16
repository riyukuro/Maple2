using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// A bought attendance day (EarlyParticipation, command 4: the event id and the coupon's uid or 0), on the KR attend
// gift's pay shape: <payAttend type="meso" price="10000" maxCount="5" couponID="30000717" />. The day's gift is
// mailed and the price (or the coupon) taken with it in one save; a short balance, another coupon, the days bought
// up or a failed save take nothing.
public class AttendancePayTests {
    private const int EventId = 170007;
    private const int RewardId = 20000001;
    private const int CouponId = 30000717;
    private const long Price = 10_000;
    private TestGameSession test = null!;

    private GameSession Start(long meso, int coupons = 0, int otherCoupons = 0) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardId));
        test.AddEvent(EventId, GameEventType.AttendGift,
            new AttendGift([new RewardItem(RewardId, 1, 1), new RewardItem(RewardId, 1, 1), new RewardItem(RewardId, 1, 1)], "gift", "title",
                "content", string.Empty, 0, null, new AttendGift.PayAttend(AttendGiftCurrencyType.Meso, Price, 5, CouponId)));
        if (coupons > 0) {
            coupon = test.Store(TestGameSession.Metadata(CouponId), amount: coupons);
        }
        if (otherCoupons > 0) {
            coupon = test.Store(TestGameSession.Metadata(CouponId + 1), amount: otherCoupons);
        }
        GameSession session = test.Start();
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [],
                [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Config = new ConfigManager(db, session);
        }
        session.Survival = new SurvivalManager(session);
        session.Currency.Meso = meso;
        test.TakeSent();
        return session;
    }

    private Item? coupon;

    private static void Buy(GameSession session, long couponUid = 0) {
        new AttendanceHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(EventId);
            writer.WriteLong(couponUid);
        }));
    }

    // A value the session holds for the event (made with the event's end as its expiry, as the handler reads it).
    private static void SetValue(GameSession session, GameEventUserValueType type, int value) {
        session.GameEvent.Get(type, EventId, session.FindEvent(EventId)!.EndTime);
        session.GameEvent.Set(EventId, type, value);
    }

    private int StoredMails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Count(mail => mail.ReceiverId == test.CharacterId);
    }

    private string? Stored(GameEventUserValueType type) => test.StoredEventValues(EventId).GetValueOrDefault(type);

    [Test]
    public void Meso_PaysThePrice_MailsTheNextGift() {
        GameSession session = Start(meso: 15_000);

        Buy(session);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5_000));
            Assert.That(StoredMails(), Is.EqualTo(1));
            Assert.That(Stored(GameEventUserValueType.AttendanceRewardsClaimed), Is.EqualTo("1"));
            Assert.That(Stored(GameEventUserValueType.AttendanceEarlyParticipationRemaining), Is.EqualTo("1"));
        });

        // A second buy with the 5,000 left is short of the price: nothing more is taken or given.
        test.TakeSent();
        Buy(session);
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5_000));
            Assert.That(StoredMails(), Is.EqualTo(1));
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Is.EqualTo(new[] { System.Convert.ToHexString(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_lackMoney).ToArray()) }));
        });
    }

    [Test]
    public void Meso_ShortOfThePrice_AnswersLackMoney() {
        GameSession session = Start(meso: Price - 1);

        Buy(session);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(Price - 1));
            Assert.That(StoredMails(), Is.EqualTo(0));
            Assert.That(test.StoredEventValues(EventId), Is.Empty);
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Is.EqualTo(new[] { System.Convert.ToHexString(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_lackMoney).ToArray()) }));
        });
    }

    // The ticked coupon box: the event's coupon is taken instead of the mesos, stored with the gift.
    [Test]
    public void Coupon_TakesTheCouponNotTheMesos() {
        GameSession session = Start(meso: 15_000, coupons: 2);

        Buy(session, coupon!.Uid);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(15_000));
            Assert.That(session.Item.Inventory.Find(CouponId).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(test.Row(coupon.Uid)?.Amount, Is.EqualTo(1), "the coupon is stored taken with the gift");
            Assert.That(StoredMails(), Is.EqualTo(1));
        });
    }

    // Another item's uid in the coupon place answers no coupon and takes nothing.
    [Test]
    public void Coupon_OfAnotherItem_AnswersHasNotCoupon() {
        GameSession session = Start(meso: 15_000, otherCoupons: 1);

        Buy(session, coupon!.Uid);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(15_000));
            Assert.That(session.Item.Inventory.Find(CouponId + 1).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(StoredMails(), Is.EqualTo(0));
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Is.EqualTo(new[] { System.Convert.ToHexString(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_hasNotCoupon).ToArray()) }));
        });
    }

    // maxCount days bought: the next buy is refused without an answer.
    [Test]
    public void DaysBoughtUp_TakeNothing() {
        GameSession session = Start(meso: 100_000);
        SetValue(session, GameEventUserValueType.AttendanceEarlyParticipationRemaining, 5);
        test.TakeSent();

        Buy(session);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(100_000));
            Assert.That(StoredMails(), Is.EqualTo(0));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // Every gift attended: nothing is left to buy.
    [Test]
    public void AllGiftsClaimed_TakeNothing() {
        GameSession session = Start(meso: 100_000);
        SetValue(session, GameEventUserValueType.AttendanceRewardsClaimed, 3);

        Buy(session);

        Assert.That(session.Currency.Meso, Is.EqualTo(100_000));
        Assert.That(StoredMails(), Is.EqualTo(0));
    }

    // A failed save gives the held mesos back and stores nothing; the next buy pays once.
    [Test]
    public void FailedSave_ReturnsThePrice() {
        GameSession session = Start(meso: 15_000);
        test.FailSave = _ => true;
        Buy(session);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(15_000));
            Assert.That(StoredMails(), Is.EqualTo(0));
            Assert.That(test.StoredEventValues(EventId), Is.Empty);
        });

        Buy(session);
        Assert.That(session.Currency.Meso, Is.EqualTo(5_000));
        Assert.That(StoredMails(), Is.EqualTo(1));
    }
}
