using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Maple2.Tools.Scheduler;
using Serilog;

namespace Maple2.Server.Tests.Game;

// AdventureProtectManager: opcode 0x131 with the state, sent at the entry and after it only when it changes.
public class AdventureProtectManagerTests {
    [Test]
    public void Update_SendsTheStateWhenItChanges() {
        var test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureProtectRequireLevel = 30 });
        GameSession session = test.Start();
        session.Player.Value.Character.Level = 29;
        test.TakeSent();

        session.AdventureProtect.Update();
        Assert.That(States(test), Is.Empty, "nothing before the entry");

        session.AdventureProtect.Load();
        session.AdventureProtect.Update();
        Assert.That(States(test), Is.EqualTo(new[] { "06" }));
        Assert.That(session.AdventureProtect.Cleared, Is.False);

        session.Player.Value.Character.Level = 30;
        session.AdventureProtect.Update();
        Assert.That(States(test), Is.EqualTo(new[] { "07" }));
        Assert.That(session.AdventureProtect.Cleared, Is.True);
    }

    // QuestManager.Complete sends the state again: the required quest is the last condition missing.
    [Test]
    public void Complete_TheRequiredQuestSendsTheState() {
        const int questId = 50001605;
        var test = new TestGameSession();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            AdventureProtectRequireLevel = 0, AdventureProtectRequireQuest = questId, AdventureProtectCharCreateTime = 0,
        });
        GameSession session = test.Start();
        test.Register(TestGameSession.QuestData(questId));
        Assume.That(session.Quest.Start(questId, bypassRequirements: true), Is.EqualTo(QuestError.none));
        Assume.That(session.Quest.TryGetQuest(questId, out Quest? quest), Is.True);
        session.AdventureProtect.Load();
        Assert.That(States(test), Is.EqualTo(new[] { "05" }), "level and age met");

        Assert.That(session.Quest.Complete(quest!, bypassConditions: true), Is.True);
        Assert.That(States(test), Is.EqualTo(new[] { "07" }));
    }

    // A character younger than AdventureProtectCharCreateTime gets the state again once that age is reached online
    // (one scheduled update on the session's queue).
    [Test]
    public void Age_ReachedOnlineSendsTheState() {
        var test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            AdventureProtectRequireLevel = 0, AdventureProtectRequireQuest = 0, AdventureProtectCharCreateTime = 72,
        });
        GameSession session = test.Start();
        var scheduler = new EventQueue(Log.Logger);
        scheduler.Start();
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(session, scheduler);
        // One second short of 72 hours: the update is due two seconds from now.
        long created = DateTime.Now.ToEpochSeconds() - 72 * 3600 + 1;
        typeof(Character).GetProperty(nameof(Character.CreationTime))!.SetValue(session.Player.Value.Character, created);
        test.TakeSent();

        session.AdventureProtect.Load();
        Assert.That(States(test), Is.EqualTo(new[] { "03" }), "not old enough yet");
        session.AdventureProtect.Load();
        Assert.That(scheduler.Count, Is.EqualTo(1), "one scheduled update");

        scheduler.InvokeAll();
        Assert.That(States(test), Is.EqualTo(new[] { "03" }), "the second Load, nothing scheduled ran");
        Thread.Sleep(2200);
        scheduler.InvokeAll();
        Assert.That(States(test), Is.EqualTo(new[] { "07" }));
    }

    private static string[] States(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.AdventureProtect)
            .Select(packet => Convert.ToHexString(packet)[4..]).ToArray();
}
