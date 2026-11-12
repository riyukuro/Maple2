using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Service;
using Maple2.Tools.Scheduler;
using Serilog;

namespace Maple2.Server.Tests.Game;

// The world boss close of one field (ChannelService.CloseWorldBoss): the boss leaves only when it was idle at the
// warning time (TimeEventRules.BossLeavesAtClose), otherwise it stays for the idle monitor. The ids are made up.
public class WorldBossCloseTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2000001;
    private const int NpcId = 23000001;
    private const int BossObjectId = 9001;
    private const long Now = 100_000_000;

    private TestGameSession test = null!;
    private FieldManager field = null!;
    private FieldNpc boss = null!;
    private ChannelService service = null!;
    private ConcurrentDictionary<int, byte> monitored = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(MapId));
        field = test.Session.Field!;
        typeof(FieldManager).GetField("Scheduler", Any)!.SetValue(field, new EventQueue(Log.Logger));
        boss = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        GC.SuppressFinalize(boss);
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(boss, BossObjectId);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc> { [BossObjectId] = boss });
        typeof(FieldManager).GetField("worldBossObjectId", Any)!.SetValue(field, BossObjectId);

        service = (ChannelService) RuntimeHelpers.GetUninitializedObject(typeof(ChannelService));
        monitored = new ConcurrentDictionary<int, byte>();
        typeof(ChannelService).GetField("monitoringBossFields", Any)!.SetValue(service, monitored);
        test.TakeSent();
    }

    private void LastHit(TimeSpan beforeNow) {
        typeof(FieldNpc).GetField("<LastDamageTick>k__BackingField", Any)!.SetValue(boss, Now - (long) beforeNow.TotalMilliseconds);
    }

    // Idle since before the warning (4 min idle 10 s before the close): the "disappeared" notice, then the boss
    // is no longer the field's world boss and its removal is queued on the field thread.
    [Test]
    public void IdleAtTheWarning_TheBossLeaves() {
        LastHit(Constant.WorldBossIdleWarningThreshold + Constant.WorldBossLeaveDelay);

        service.CloseWorldBoss(field, NpcId, MapId, Now);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice }));
        Assert.That(field.GetWorldBossNpc(), Is.Null);
        Assert.That(monitored, Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // Never hit counts as idle.
    [Test]
    public void NeverHit_TheBossLeaves() {
        service.CloseWorldBoss(field, NpcId, MapId, Now);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice }));
        Assert.That(field.GetWorldBossNpc(), Is.Null);
    }

    // Hit 3:55 before the warning time: the warning test (4 min) did not warn, so it must not leave either.
    // The field is already watched here, so no second monitor starts.
    [Test]
    public void HitBeforeTheWarning_TheBossStays() {
        LastHit(TimeSpan.FromSeconds(235) + Constant.WorldBossLeaveDelay);
        monitored[MapId] = 0;

        service.CloseWorldBoss(field, NpcId, MapId, Now);

        Assert.That(test.TakeSentOps(), Is.Empty);
        Assert.That(field.GetWorldBossNpc(), Is.SameAs(boss));
        Assert.That(monitored.Keys, Is.EqualTo(new[] { MapId }));
        Assert.That(test.Errors, Is.Empty);
    }

    // No boss in the field (killed before the close): nothing.
    [Test]
    public void NoBoss_Nothing() {
        field.DespawnWorldBoss();
        test.TakeSent();

        service.CloseWorldBoss(field, NpcId, MapId, Now);

        Assert.That(test.TakeSentOps(), Is.Empty);
        Assert.That(monitored, Is.Empty);
    }
}
