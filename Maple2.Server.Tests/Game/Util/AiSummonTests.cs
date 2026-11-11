using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
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

    [TestCase(30, 30)]
    [TestCase(180000, 180000)]
    [TestCase(0, 0)]
    [TestCase(-1, 0)]
    public void LifeTimeIsSeconds(int lifeTime, int seconds) {
        Assert.That(AiSummon.LifeTime(lifeTime), Is.EqualTo(TimeSpan.FromSeconds(seconds)));
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
}
