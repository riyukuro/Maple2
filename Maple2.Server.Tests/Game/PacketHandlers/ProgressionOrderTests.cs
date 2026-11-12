using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Game;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The answers of a level up, a prestige level up, a stat point reset and the job script's confirm (Job 2),
// in the order KMS2 sends them.
public class ProgressionOrderTests {
    private static List<SendOp> Only(IEnumerable<SendOp> ops, params SendOp[] kept) {
        return ops.Where(kept.Contains).ToList();
    }

    // The config manager of a test session is not loaded: its session, an empty stat point book and the skill
    // tree of a job table whose beginner row has no skill.
    internal static void LoadConfig(TestGameSession test, GameSession session) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
            [JobCode.Knight] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        typeof(ConfigManager).GetField("session", any)!.SetValue(session.Config, session);
        var limits = new Dictionary<string, int> {
            ["StatPointLimit_str"] = 100, ["StatPointLimit_dex"] = 100, ["StatPointLimit_int"] = 100,
            ["StatPointLimit_luk"] = 100, ["StatPointLimit_hp"] = 100, ["StatPointLimit_cap"] = 100,
        };
        typeof(ConfigManager).GetField("statLimits", any)!.SetValue(session.Config, limits);
        typeof(ConfigManager).GetField("statAttributes", any)!.SetValue(session.Config, new StatAttributes(limits));
        typeof(ConfigManager).GetField("hotBars", any)!.SetValue(session.Config, new List<HotBar>());
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill), any)!.SetValue(session.Config, new SkillManager(session, new SkillBook()));
    }

    [Test]
    public void StatReset_SendsTheAllocationThenTheNotice() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        LoadConfig(test, session);
        session.Config.AddStatPoint(AttributePointSource.Quest, 3);
        test.TakeSent();
        var handler = new AttributePointHandler();
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteByte((byte) BasicAttribute.Strength);
            writer.WriteInt(2);
        }));
        // The stat block follows each answer in the server; the test session's stats have no actor and send none.
        Assert.That(Only(test.TakeSentOps(), SendOp.AttributePoint, SendOp.Notice, SendOp.Stat),
            Is.EqualTo(new[] { SendOp.AttributePoint }));

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Only(sent.Select(TestGameSession.Op), SendOp.AttributePoint, SendOp.Notice, SendOp.Stat),
            Is.EqualTo(new[] { SendOp.AttributePoint, SendOp.Notice }));
        // AttributePoint 1: the total, then no spent stat.
        byte[] allocation = sent.First(packet => TestGameSession.Op(packet) == SendOp.AttributePoint);
        Assert.That(Convert.ToHexString(allocation.AsSpan(2)), Is.EqualTo("010300000000000000"));

        // The client sends no reset with nothing spent: one is not answered.
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));
        Assert.That(test.TakeSentOps(), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void LevelUp_SendsTheLevelThenTheSkillTree() {
        var test = new TestGameSession();
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long> {
            [1] = 100,
            [2] = 100000,
        }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { characterMaxLevel = 70, UserRevivalPaneltyMinLevel = 10 });
        GameSession session = test.Start();
        LoadConfig(test, session);
        test.TakeSent();

        session.Exp.AddStaticExp(150);

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(2));
        List<byte[]> sent = test.TakeSent();
        // The penalty clear comes first even under the penalty's min level, the hot bars after the skill tree (the
        // test session's stats send no block between them), the trophies after those.
        List<SendOp> ops = Only(sent.Select(TestGameSession.Op), SendOp.ExpUp, SendOp.RevivalConfirm, SendOp.LevelUp, SendOp.Job, SendOp.KeyTable);
        Assert.That(ops, Is.EqualTo(new[] { SendOp.ExpUp, SendOp.RevivalConfirm, SendOp.LevelUp, SendOp.Job, SendOp.KeyTable }));
        List<SendOp> all = sent.Select(TestGameSession.Op).ToList();
        if (all.Contains(SendOp.Achieve)) {
            Assert.That(all.IndexOf(SendOp.Achieve), Is.GreaterThan(all.IndexOf(SendOp.KeyTable)));
        }
        byte[] keys = sent.First(packet => TestGameSession.Op(packet) == SendOp.KeyTable);
        Assert.That(keys[2], Is.EqualTo(7));
        // Job: the player's object id, then mode 1.
        byte[] job = sent.First(packet => TestGameSession.Op(packet) == SendOp.Job);
        Assert.That(BitConverter.ToInt32(job, 2), Is.EqualTo(session.Player.ObjectId));
        Assert.That(job[6], Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void PrestigeLevelUp_SendsTheLevelAndTheMissionsFirst() {
        var test = new TestGameSession();
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        test.SetTable(new PrestigeMissionTable(new Dictionary<int, PrestigeMissionMetadata>()));
        GameSession session = test.Start();
        LoadConfig(test, session);
        test.TakeSent();

        session.Exp.PrestigeLevelUp();

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Take(2).Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Prestige, SendOp.Prestige }));
        Assert.That(sent.Take(2).Select(packet => packet[2]), Is.EqualTo(new byte[] { 2, 7 }));
        Assert.That(test.Errors, Is.Empty);
    }

    [TestCase(JobCode.Knight, true)]
    [TestCase(JobCode.SoulBinder, true)]
    [TestCase(JobCode.None, false)]
    [TestCase(JobCode.Newbie, false)]
    [TestCase(JobCode.GameMaster, false)]
    [TestCase((JobCode) 250, false)]
    public void OnlyARowWithAJobChangesTheJob(JobCode changeTo, bool expected) {
        Assert.That(JobRules.IsJobChange(changeTo), Is.EqualTo(expected));
    }

    [Test]
    public void TheJobChangeRepeatsTheRowsJobAndLevel() {
        Assert.That(JobRules.AdvanceError(JobCode.Newbie, 10, JobCode.Newbie, 10), Is.Null);
        Assert.That(JobRules.AdvanceError(JobCode.Newbie, 10, JobCode.Newbie, 9), Is.EqualTo(JobError.s_err_job_not_enough_level));
        Assert.That(JobRules.AdvanceError(JobCode.Newbie, 10, JobCode.Knight, 50), Is.EqualTo(JobError.s_err_job_bad_job));
        Assert.That(JobRules.AdvanceError(JobCode.None, 0, JobCode.Knight, 1), Is.Null);
    }

    // Without a job script open the confirm changes nothing.
    [Test]
    public void JobConfirm_WithoutAScriptIsIgnored() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        new JobHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(30736);
        }));

        Assert.That(test.TakeSentOps(), Is.Empty);
        Assert.That(session.Player.Value.Character.Job, Is.EqualTo(Job.Newbie));
        Assert.That(test.Errors, Is.Empty);
    }
}
