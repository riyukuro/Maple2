using System.Collections.Generic;
using System.Reflection;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// At a loop end of a held motion the client sends the repeat (a tick sync) only when the skill's cooldown has run
// out or a recharge use is ready, and holds the loop until one is (StateSkill, stateskill.cpp:917-927); a skill that
// continues while held or a charging skill repeats with no wait. The repeat restarts no cooldown and pays nothing; a
// recharge skill's repeat takes a use. Before, any sync of a looping motion made the server forget what the cast's
// point groups hit, cooldown or not, and took no recharge use. The skill ids are made up (in the job id range, so the
// cooldown is checked as a job skill's at a cast).
public class TickSyncCooldownTests : TriggerTestBase {
    private const int Skill = 10900911;
    private const string Sequence = "Attack_Hold_A";
    private static readonly SkillHitShape GroupShape = new(Arrow: false, Targeted: false, Max: 5, Returns: false, Repierce: false, PointGroup: 1);

    private void SetUp(float cooldownSeconds, int rechargeMaxCount = 0, bool continueSkill = false, bool chargingSkill = false) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(Skill);
        SkillMetadataMotion motion = skill.Data.Motions[0];
        QuestConditionWiringTests.SetSkills(test, skill with {
            Property = skill.Property with { ContinueSkill = continueSkill },
            State = skill.State with { RechargeMaxCount = rechargeMaxCount, ChargingSkill = chargingSkill },
            Data = skill.Data with {
                Condition = skill.Data.Condition with { CooldownTime = cooldownSeconds },
                Motions = [motion with { MotionProperty = motion.MotionProperty with { SequenceName = Sequence } }],
            },
        });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        test.Session.Player.Stats.Values[BasicAttribute.AttackSpeed].Total = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        // The motion loops for 1 s.
        var sequences = new Dictionary<string, AnimationSequenceMetadata> {
            [Sequence] = new(Sequence, 1, 2f, [new AnimationKey("loopstart", 0.5f), new AnimationKey("loopend", 1.5f), new AnimationKey("end", 2f)]),
        };
        typeof(AnimationManager).GetField(nameof(AnimationManager.RigMetadata))!
            .SetValue(test.Session.Player.Animation, new AnimationMetadata("male", sequences));
        SetTick(1_000_000);
        test.TakeSent();
    }

    private void SetTick(long tick) => typeof(FieldManager).GetField("<FieldTick>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Field, tick);

    private SkillHandler Handler => new() { SkillMetadata = Field.SkillMetadata, TableMetadata = null! };

    private SkillRecord Cast() {
        Handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(Skill);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(0);
            writer.WriteLong(0);
        }));
        SkillRecord? record = test.Session.Player.ActiveSkills.Get(1);
        Assert.That(record, Is.Not.Null, "the cast runs");
        Assert.That(record!.Hits.TakePoint(0, 0, held: false, count: 5, tick: Field.FieldTick), Is.True);
        return record;
    }

    // A sync at this many ms after the cast: whether it forgot the point group's hit on object 101 (one more hit pays).
    private bool SyncForgets(SkillRecord record, long afterMs, long attackUid) {
        SetTick(1_000_000 + afterMs);
        Handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
        }));
        return Hit(record, attackUid);
    }

    private static bool Hit(SkillRecord record, long attackUid) {
        record.Hits.Declare(attackUid, 0, 0);
        return record.Hits.TakeTarget(attackUid, 0, 0) && record.Hits.TakeHit(attackUid, 101, 0, GroupShape) == HitRefusal.None;
    }

    [Test]
    public void ARepeatWithinTheCooldown_KeepsTheHits() {
        SetUp(cooldownSeconds: 8);
        SkillRecord record = Cast();
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        Assert.Multiple(() => {
            Assert.That(SyncForgets(record, 4000, 2), Is.False, "the client holds the loop until the cooldown ends");
            Assert.That(SyncForgets(record, 8100, 3), Is.True, "after the cooldown it repeats");
            Assert.That(SyncForgets(record, 9100, 4), Is.True, "the repeat restarted no cooldown");
        });
    }

    // A repeat that only arrives early draws on the lag budget, as a cast does.
    [Test]
    public void ARepeatSlightlyEarly_IsTaken() {
        SetUp(cooldownSeconds: 8);
        SkillRecord record = Cast();
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        Assert.That(SyncForgets(record, 7500, 2), Is.True);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void AContinueOrChargingSkill_RepeatsWithinTheCooldown(bool continueSkill, bool chargingSkill) {
        SetUp(cooldownSeconds: 8, continueSkill: continueSkill, chargingSkill: chargingSkill);
        SkillRecord record = Cast();
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        Assert.That(SyncForgets(record, 1000, 2), Is.True);
    }

    // Two uses: the cast takes one, the first repeat the other, and the next repeat waits for a use to come back.
    [Test]
    public void ARechargeSkillsRepeat_TakesAUse() {
        SetUp(cooldownSeconds: 8, rechargeMaxCount: 2);
        SkillRecord record = Cast();
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        Assert.Multiple(() => {
            Assert.That(SyncForgets(record, 1000, 2), Is.True, "a use is left");
            Assert.That(test.Session.Config.GetSkillCooldown(Skill)?.Charges, Is.EqualTo(2), "the repeat took it");
            Assert.That(SyncForgets(record, 2000, 3), Is.False, "no use left");
            Assert.That(SyncForgets(record, 8100, 4), Is.True, "a use came back");
        });
    }

    // A skill that continues while held repeats with no wait and takes a recharge use at each loop end (the client's
    // else branch: OnRechargeSkillUsed at every repeat). Three uses: the cast and two repeats take them all; the next
    // repeat is still taken, the uses only counted. Before, its repeats took none.
    [Test]
    public void AContinueSkillsRepeat_TakesAUseButIsNeverHeld() {
        SetUp(cooldownSeconds: 8, rechargeMaxCount: 3, continueSkill: true);
        SkillRecord record = Cast();
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        Assert.Multiple(() => {
            Assert.That(SyncForgets(record, 1000, 2), Is.True, "the first repeat");
            Assert.That(SyncForgets(record, 2000, 3), Is.True, "the second repeat");
            Assert.That(test.Session.Config.GetSkillCooldown(Skill)?.Charges, Is.EqualTo(3), "the cast and both repeats took a use");
            Assert.That(SyncForgets(record, 3000, 4), Is.True, "with no use left the repeat is not held");
        });
    }

    // A repeat early on both the cooldown and the loop draws once from the lag budget, for the longer, as a cast does;
    // before, it drew for each.
    [Test]
    public void ARepeatEarlyOnCooldownAndLoop_DrawsTheLagBudgetOnce() {
        SetUp(cooldownSeconds: 1.2f);
        SkillRecord record = Cast();
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        long syncTick = 1_000_000 + 200;
        long loopEarly = record.Hits.SyncEarlyBy(syncTick, test.Session.Config.HeldInterval(record.Motion.MotionProperty, record.CastUid));
        long before = test.Session.Config.Lag.Level(syncTick);
        Assert.That(SyncForgets(record, 200, 2), Is.True, "the repeat only arrives early");
        long drawn = before - test.Session.Config.Lag.Level(syncTick);
        // The 1.2 s cooldown is 500 ms short 200 ms after the cast (past the cast slack), the 1 s loop 180 ms.
        Assert.That(loopEarly, Is.EqualTo(180), "the repeat comes before the loop ends");
        Assert.That(drawn, Is.EqualTo(500), "one draw for the longer (two drew 680)");
    }

    // The ingest reads chargingSkill from the played client's <basic><combo> (made-up rows).
    [Test]
    public void TheIngestReadsChargingSkillFromThePlayedClient() {
        var document = new System.Xml.XmlDocument();
        document.LoadXml("""
            <ms2>
              <skill id="100"><basic><combo chargingSkill="1" inputSkill="100" /></basic></skill>
              <skill id="101"><basic><combo comboSkill="1" /></basic></skill>
            </ms2>
            """);
        Dictionary<int, PlayedClientSkills.Entry> read = PlayedClientSkills.Read([document]);
        Assert.Multiple(() => {
            Assert.That(read[100].ChargingSkill, Is.True);
            Assert.That(read[101].ChargingSkill, Is.False);
            Assert.That(PlayedClientSkills.BuildState(100, (System.Xml.XmlElement) document.SelectSingleNode("ms2/skill")!).ChargingSkill, Is.True);
        });
    }
}
