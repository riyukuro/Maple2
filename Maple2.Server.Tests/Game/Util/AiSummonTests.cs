using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Xml.AI;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The AI summon node, as the server AI files and the npc rows' summonNpcIDs give it.
public class AiSummonTests {
    // npc 21420503: aiInfo AI_Default_Summon_24003227.xml, summonNpcIDs="24003227"; the template's node is npcId="1".
    [TestCase(1, 24003227)]
    [TestCase(2, 21420305)]
    [TestCase(3, null)]
    [TestCase(0, null)]
    [TestCase(-1, null)]
    public void NpcIdIsAOneBasedIndex(int index, int? expected) {
        int[] ids = index == 1 ? [24003227] : [21420302, 21420305];
        Assert.That(AiSummon.NpcId(ids, index), Is.EqualTo(expected));
    }

    [Test]
    public void NoListNoSummon() {
        Assert.That(AiSummon.NpcId(null, 1), Is.Null);
    }

    // npcCount per run, at most npcCountMax alive in the group.
    [TestCase(1, 1, 0, 1)]
    [TestCase(1, 1, 1, 0)]
    [TestCase(3, 30, 0, 3)]
    [TestCase(3, 30, 28, 2)]
    [TestCase(3, 30, 30, 0)]
    [TestCase(7, 200, 199, 1)]
    [TestCase(2, 0, 0, 2)]
    [TestCase(0, 1, 0, 1)]
    [TestCase(1, 1, 5, 0)]
    public void Count(int npcCount, int npcCountMax, int alive, int expected) {
        Assert.That(AiSummon.Count(npcCount, npcCountMax, alive), Is.EqualTo(expected));
    }

    private static readonly Vector3 Summoner = new(1000, 2000, 1500);

    // AI_Blackbean_Phase01_Hard: summonPos="-2850, 6000, 1500" is a map position.
    [Test]
    public void SummonPosIsAMapPosition() {
        Vector3 position = AiSummon.Position(Summoner, new Vector3(-2850, 6000, 1500), Vector3.Zero, Vector3.Zero, new Random(1));
        Assert.That(position, Is.EqualTo(new Vector3(-2850, 6000, 1500)));
    }

    // AI_Blackbean_Phase01_Normal: summonPosOffset="-450, 450, 0" is from the summoner; no position: at the summoner.
    [Test]
    public void OffsetIsFromTheSummoner() {
        Assert.Multiple(() => {
            Assert.That(AiSummon.Position(Summoner, Vector3.Zero, new Vector3(-450, 450, 0), Vector3.Zero, new Random(1)),
                Is.EqualTo(new Vector3(550, 2450, 1500)));
            Assert.That(AiSummon.Position(Summoner, Vector3.Zero, Vector3.Zero, Vector3.Zero, new Random(1)), Is.EqualTo(Summoner));
        });
    }

    // summonRadius="400, 400, 0" scatters on the ground within the radius; a Z radius moves nothing up.
    [Test]
    public void RadiusScattersOnTheGround() {
        var random = new Random(7);
        for (int i = 0; i < 200; i++) {
            Vector3 position = AiSummon.Position(Summoner, Vector3.Zero, Vector3.Zero, new Vector3(400, 200, 250), random);
            float x = (position.X - Summoner.X) / 400;
            float y = (position.Y - Summoner.Y) / 200;
            Assert.That(x * x + y * y, Is.LessThanOrEqualTo(1.0001f));
            Assert.That(position.Z, Is.EqualTo(Summoner.Z));
        }
        Assert.That(AiSummon.Position(Summoner, Vector3.Zero, Vector3.Zero, new Vector3(0, 0, 250), random), Is.EqualTo(Summoner));
    }

    [Test]
    public void Rotation() {
        var facing = new Vector3(0, 0, 135);
        Assert.Multiple(() => {
            Assert.That(AiSummon.Rotation(facing, Vector3.Zero, new Vector3(0, 0, 90)), Is.EqualTo(new Vector3(0, 0, 90)));
            Assert.That(AiSummon.Rotation(facing, Vector3.Zero, Vector3.Zero), Is.EqualTo(facing));
            Assert.That(AiSummon.Rotation(facing, new Vector3(-2850, 6000, 1500), Vector3.Zero), Is.EqualTo(Vector3.Zero));
        });
    }

    // Under 1000 seconds, from 1000 milliseconds; AI_DevilHugeRed's "15.6" is parsed as 15600.
    [TestCase(30, 30000)]
    [TestCase(80, 80000)]
    [TestCase(999, 999000)]
    [TestCase(1000, 1000)]
    [TestCase(6000, 6000)]
    [TestCase(15600, 15600)]
    [TestCase(180000, 180000)]
    [TestCase(0, 0)]
    [TestCase(-1, 0)]
    public void LifeTimeUnit(int lifeTime, int milliseconds) {
        Assert.That(AiSummon.LifeTime(lifeTime), Is.EqualTo(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [TestCase(1500, 1500)]
    [TestCase(0, 0)]
    public void DelayIsMilliseconds(int delayTick, int milliseconds) {
        Assert.That(AiSummon.Delay(delayTick), Is.EqualTo(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [TestCase(NodeSummonMaster.Master, true)]
    [TestCase(NodeSummonMaster.Slave, true)]
    [TestCase(NodeSummonMaster.None, false)]
    public void LeavesWithSummoner(NodeSummonMaster master, bool expected) {
        Assert.That(AiSummon.LeavesWithSummoner(master), Is.EqualTo(expected));
    }

    [Test]
    public void IngestReadsSummonNpcIds() {
        Assert.Multiple(() => {
            Assert.That(NpcMapper.ParseSummonNpcIds("<npc><environment><aiInfo path=\"AI_DevilHugeBlue.xml\" summonNpcIDs=\"21404189,21403189\" /></environment></npc>"),
                Is.EqualTo(new[] { 21404189, 21403189 }));
            Assert.That(NpcMapper.ParseSummonNpcIds("<npc><aiInfo path=\"AI_DefaultNew.xml\" /></npc>"), Is.Null);
            Assert.That(NpcMapper.ParseSummonNpcIds("<npc><aiInfo summonNpcIDs=\"\" /></npc>"), Is.Null);
        });
    }
    // Slaves are the summons that leave with the summoner; a group filter counts only that group.
    [TestCase(101, true, null, true)]
    [TestCase(101, false, null, false)]
    [TestCase(201, true, 201, true)]
    [TestCase(101, true, 201, false)]
    [TestCase(201, false, 201, false)]
    public void CountedSlave(int summonGroup, bool leavesWithSummoner, int? group, bool expected) {
        Assert.That(AiSummon.IsCountedSlave(summonGroup, leavesWithSummoner, group), Is.EqualTo(expected));
    }

    // The Yeti bosses test count 0..4 in order (first passing wins); only an exact count picks each branch.
    [Test]
    public void SlaveCountIsExact() {
        int[] counts = [0, 1, 2, 3, 4];
        Assert.Multiple(() => {
            for (int slaves = 0; slaves <= 4; slaves++) {
                int alive = slaves;
                Assert.That(counts.First(count => AiSummon.SlaveCountPasses(count, alive)), Is.EqualTo(slaves));
            }
            Assert.That(counts.Any(count => AiSummon.SlaveCountPasses(count, 5)), Is.False);
        });
    }

    // AI_Primadonna tests count 2, then count 1 (berserk), for group 201 with npcCountMax 2.
    [Test]
    public void PrimadonnaReachesItsBerserkBranch() {
        int[] counts = [2, 1];
        Assert.Multiple(() => {
            Assert.That(counts.First(count => AiSummon.SlaveCountPasses(count, 2)), Is.EqualTo(2));
            Assert.That(counts.First(count => AiSummon.SlaveCountPasses(count, 1)), Is.EqualTo(1));
            Assert.That(counts.Any(count => AiSummon.SlaveCountPasses(count, 0)), Is.False);
        });
    }

    // Seolnunyi: slaveCount="0" slaveCountOp="Greater" passes while any slave lives.
    [TestCase(AiConditionOp.Greater, 0, 0, false)]
    [TestCase(AiConditionOp.Greater, 0, 1, true)]
    [TestCase(AiConditionOp.Equal, 2, 2, true)]
    [TestCase(AiConditionOp.Less, 2, 1, true)]
    [TestCase(AiConditionOp.Less, 2, 2, false)]
    [TestCase(AiConditionOp.GreaterEqual, 2, 2, true)]
    [TestCase(AiConditionOp.LessEqual, 2, 3, false)]
    public void SlaveCountOp(AiConditionOp op, int value, int slaves, bool expected) {
        Assert.That(AiSummon.SlaveCountOpPasses(op, value, slaves), Is.EqualTo(expected));
    }

    // The parser reads both condition forms into one class; the op form must not become a count="0" test.
    [Test]
    public void IngestKeepsBothSlaveCountForms() {
        var countForm = new SlaveCountCondition { count = 2, useSummonGroup = true, summonGroup = 201 };
        var opForm = new SlaveCountCondition { slaveCount = 0, slaveCountOp = ConditionOp.Greater };
        AiMetadata.Condition count = AiMapper.MapSlaveCount("SlaveCount", [], countForm);
        AiMetadata.Condition op = AiMapper.MapSlaveCount("slaveCount", [], opForm);
        Assert.Multiple(() => {
            Assert.That(count, Is.TypeOf<AiMetadata.SlaveCountCondition>());
            var countCondition = (AiMetadata.SlaveCountCondition) count;
            Assert.That((countCondition.Count, countCondition.UseSummonGroup, countCondition.SummonGroup), Is.EqualTo((2, true, 201)));
            Assert.That(op, Is.TypeOf<AiMetadata.SlaveCountOpCondition>());
            var opCondition = (AiMetadata.SlaveCountOpCondition) op;
            Assert.That(opCondition.SlaveCount, Is.EqualTo(0));
            Assert.That(opCondition.SlaveCountOp, Is.EqualTo(AiConditionOp.Greater));
        });
    }

    // SetMasterValue / SetSlaveValue: isModify adds to a key the npc has; a missing key, or no isModify, sets it.
    [TestCase(true, 5, 3, true, 8)]
    [TestCase(true, 5, -1, true, 4)]
    [TestCase(false, 0, 3, true, 3)]
    [TestCase(true, 5, 3, false, 3)]
    [TestCase(false, 0, 1, false, 1)]
    public void SetValueModify(bool hasOld, int oldValue, int value, bool isModify, int expected) {
        Assert.That(AiSummon.ModifiedValue(hasOld, oldValue, value, isModify), Is.EqualTo(expected));
    }

    // SetSlaveValue: every slave gets the value; isRandom picks one of them.
    [Test]
    public void SlaveValueTargets() {
        int[] slaves = [11, 12];
        var random = new Random(7);
        Assert.Multiple(() => {
            Assert.That(AiSummon.ValueTargets(slaves, false, random), Is.EqualTo(slaves));
            for (int i = 0; i < 20; i++) {
                IReadOnlyList<int> one = AiSummon.ValueTargets(slaves, true, random);
                Assert.That(one, Has.Count.EqualTo(1));
                Assert.That(slaves, Does.Contain(one[0]));
            }
            Assert.That(Enumerable.Range(0, 50).Select(_ => AiSummon.ValueTargets(slaves, true, random)[0]).Distinct().Count(), Is.EqualTo(2));
            Assert.That(AiSummon.ValueTargets(Array.Empty<int>(), true, random), Is.Empty);
        });
    }

    // KR AI_GigantikaTail_phase_1.xml summons its next body with option="masterHP,hitDamage" and then RemoveMe; the
    // phases share one full health (1104012854, npc 23001001 to 23001003). The next body starts at the health the
    // last one had; without masterHP (Madria's second summon, group 992) a summon keeps its own full health.
    [Test]
    public void MasterHp_TheSummonTakesOverTheSummonersHealth() {
        Assert.Multiple(() => {
            Assert.That(AiSummon.InheritedHealth([NodeSummonOption.MasterHp, NodeSummonOption.HitDamage], 99361157, 1104012854), Is.EqualTo(99361157));
            Assert.That(AiSummon.InheritedHealth([NodeSummonOption.MasterHp], 2000, 1000), Is.EqualTo(1000));
            Assert.That(AiSummon.InheritedHealth([NodeSummonOption.MasterHp], 0, 1000), Is.EqualTo(1));
            Assert.That(AiSummon.InheritedHealth([], 500, 1000), Is.Null);
            Assert.That(AiSummon.InheritedHealth([NodeSummonOption.LinkHp], 500, 1000), Is.Null);
        });
    }

    // KR AI_TurkaHoodForce_Phase01.xml (npc 23000120, full health 61888412906) summons its two copies with
    // option="masterHP,linkHP", commented as health shared with the summoner. A hit on any member (or a heal) moves the
    // one health all members hold; it stays within 0 and the summoner's full health.
    [Test]
    public void LinkHp_TheMembersShareOneHealth() {
        const long total = 61888412906;
        Assert.Multiple(() => {
            Assert.That(AiSummon.LinksHealth([NodeSummonOption.MasterHp, NodeSummonOption.LinkHp]), Is.True);
            Assert.That(AiSummon.LinksHealth([NodeSummonOption.MasterHp, NodeSummonOption.HitDamage]), Is.False);
            // The summoner takes 1000 and a copy 500: all three hold 30000000000 - 1500.
            Assert.That(AiSummon.SharedHealth(30000000000, total, [(29999999000, 30000000000), (29999999500, 30000000000), (30000000000, 30000000000)]),
                Is.EqualTo(29999998500));
            // Nothing changed: the shared health stays.
            Assert.That(AiSummon.SharedHealth(5000, total, [(5000, 5000), (5000, 5000)]), Is.EqualTo(5000));
            // A copy's lethal hit takes the shared health to 0, so every member dies.
            Assert.That(AiSummon.SharedHealth(5000, total, [(5000, 5000), (0, 5000)]), Is.EqualTo(0));
            Assert.That(AiSummon.SharedHealth(5000, total, [(1000, 5000), (2000, 5000)]), Is.EqualTo(0));
            // A heal past full health stops at the summoner's full health.
            Assert.That(AiSummon.SharedHealth(total - 10, total, [(total, total - 10), (total - 10, total - 10)]), Is.EqualTo(total));
        });
    }
}
