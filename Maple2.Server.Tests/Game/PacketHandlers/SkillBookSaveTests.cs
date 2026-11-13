using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The skill book's Save Skills: the Job 9 answer that carries the client's "learned" notice, and the SkillBookTree
// save's rank, which the client sends in its own numbering (0 every rank, 1 base job, 2 awakening).
public class SkillBookSaveTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int SkillId = 10500001;

    // A beginner whose tree has one base-job active skill (not a base skill: it starts at 0 of 10).
    private static GameSession Setup(TestGameSession test) {
        GameSession session = test.Start();
        ProgressionOrderTests.LoadConfig(test, session);
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [new JobTable.Skill(SkillId, [], 10, 0)],
                [SkillRank.Awakening] = [],
            }, []),
        }));
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(skills)!;
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)) with {
            Type = SkillType.Active,
            MaxLevel = 10,
        };
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel));
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(SkillId, (short) 1), new SkillMetadata(SkillId, 1, null, property, null!, level)]);
        typeof(GameSession).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(session, skills);
        typeof(ConfigManager).GetField("hotBars", Any)!.SetValue(session.Config, new List<HotBar> { new() });
        typeof(ConfigManager).GetField("skillPoints", Any)!.SetValue(session.Config, new SkillPoint());
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill), Any)!.SetValue(session.Config, new SkillManager(session, new SkillBook()));
        test.TakeSent();
        return session;
    }

    private static byte[] JobUpdate(TestGameSession test) {
        byte[] reply = test.TakeSent().Find(packet => TestGameSession.Op(packet) == SendOp.Job)!;
        Assert.That(reply, Is.Not.Null);
        return reply;
    }

    // Job 9 with the skill at level 1: the answer marks it changed once, which is what the client shows the
    // "learned" notice for (CSceneStateGame::OnJob, modes 9 and 11); the same tree again is not a new skill.
    [Test]
    public void Update_FirstPointMarksTheSkillChangedOnce() {
        var test = new TestGameSession();
        GameSession session = Setup(test);
        var handler = new JobHandler();
        // Job 9: count, then id, level, learned.
        IByteReader Request() => TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(1);
            writer.WriteInt(SkillId);
            writer.WriteShort(1);
            writer.WriteBool(true);
        });

        handler.Handle(session, Request());
        byte[] first = JobUpdate(test);
        // op, object id, mode, rank, job code, count, then id, level, changed, learned, extra.
        Assert.That(first.Length, Is.EqualTo(27));
        Assert.That(first[6], Is.EqualTo(9));
        Assert.That(first[15], Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(first, 16), Is.EqualTo(SkillId));
        Assert.That(BitConverter.ToInt32(first, 20), Is.EqualTo(1));
        Assert.That(first.AsSpan(24, 3).ToArray(), Is.EqualTo(new byte[] { 1, 1, 0 }));

        handler.Handle(session, Request());
        byte[] second = JobUpdate(test);
        Assert.That(second.AsSpan(24, 3).ToArray(), Is.EqualTo(new byte[] { 0, 1, 0 }));
        Assert.That(session.Config.Skill.SkillInfo.GetMainSkill(SkillId)!.Level, Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // SkillBookTree 1 for tab 7 with the base-job skill at 3 points. The rank is the client's: 0 and 1 cover the
    // base job, 2 only the awakening. The answer repeats the client's number (it commits that rank of its tree).
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    public void Save_ReadsTheClientRank(int clientRank, bool stored) {
        var test = new TestGameSession();
        GameSession session = Setup(test);
        session.Config.Skill.SkillBook.SkillTabs.Add(new SkillTab("tab") { Id = 7 });
        var tab = new SkillTab("tab") { Id = 7 };
        tab.Skills[SkillId] = 3;

        new SkillBookHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(7);
            writer.WriteLong(7);
            writer.WriteInt(clientRank);
            writer.WriteInt(1);
            writer.WriteClass<SkillTab>(tab);
        }));

        Dictionary<int, int> saved = session.Config.Skill.SkillBook.SkillTabs[0].Skills;
        Assert.That(saved.ContainsKey(SkillId), Is.EqualTo(stored));
        byte[] reply = test.TakeSent().Find(packet => TestGameSession.Op(packet) == SendOp.SkillBookTree)!;
        Assert.That(reply, Is.Not.Null);
        // op, mode 1, active tab, saved tab, rank.
        Assert.That(reply[2], Is.EqualTo(1));
        Assert.That(BitConverter.ToInt64(reply, 11), Is.EqualTo(7));
        Assert.That(BitConverter.ToInt32(reply, 19), Is.EqualTo(clientRank));
    }

    [Test]
    public void Save_UnknownRankIsIgnored() {
        var test = new TestGameSession();
        GameSession session = Setup(test);
        new SkillBookHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(0);
            writer.WriteLong(0);
            writer.WriteInt(3);
        }));
        Assert.That(test.TakeSentOps(), Is.Empty);
    }
}
