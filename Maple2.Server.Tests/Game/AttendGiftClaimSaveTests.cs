using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// An AttendGift event with no play time is attended at BeginTimer (the client sends no claim for it). The reward
// mail, its item and the day's claim are stored in one transaction: a crash between the mail and the claim used to
// keep the gift and lose the claim, so it was given again after the restart.
public class AttendGiftClaimSaveTests {
    private const int EventId = 7102;
    private const int RewardId = 20000001;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardId));
        test.AddEvent(EventId, GameEventType.AttendGift,
            new AttendGift([new RewardItem(RewardId, 1, 1), new RewardItem(RewardId, 1, 1)], "gift", "title", "content", string.Empty, 0, null));
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
        test.TakeSent();
        return session;
    }

    private static void BeginTimer(GameSession session) {
        new AttendanceHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));
    }

    private int StoredMails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Count(mail => mail.ReceiverId == test.CharacterId);
    }

    // The claim is stored with the mail at once, not left to the next session save.
    [Test]
    public void BeginTimer_StoresTheClaimWithTheMail() {
        GameSession session = Start();

        BeginTimer(session);

        Dictionary<GameEventUserValueType, string> stored = test.StoredEventValues(EventId);
        Assert.That(StoredMails(), Is.EqualTo(1));
        Assert.That(stored[GameEventUserValueType.AttendanceRewardsClaimed], Is.EqualTo("1"));
        Assert.That(stored.ContainsKey(GameEventUserValueType.AttendanceCompletedTimestamp), Is.True);
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.AttendanceRewardsClaimed, EventId), Is.EqualTo("1"));

        // The day is attended: a second BeginTimer gives nothing.
        BeginTimer(session);
        Assert.That(StoredMails(), Is.EqualTo(1));
    }

    // A save that fails stores nothing and claims nothing in memory; the next BeginTimer gives the day's gift once.
    [Test]
    public void BeginTimer_FailedSave_StoresAndClaimsNothing() {
        GameSession session = Start();
        test.FailSave = _ => true;
        BeginTimer(session);
        test.FailSave = null;

        Assert.That(StoredMails(), Is.EqualTo(0));
        Assert.That(test.StoredEventValues(EventId), Is.Empty);
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.AttendanceRewardsClaimed, EventId), Is.Null.Or.Empty.Or.EqualTo("0"));

        BeginTimer(session);
        Assert.That(StoredMails(), Is.EqualTo(1));
        Assert.That(test.StoredEventValues(EventId)[GameEventUserValueType.AttendanceRewardsClaimed], Is.EqualTo("1"));
    }
}
