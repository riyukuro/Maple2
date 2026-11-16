using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Packets;

// The Job packet read as CSceneStateGame::OnJob (scenestategame.cpp, the modes 0, 1, 2, 7, 8, 9, 10 and 11 hand the packet
// to JobSystem::vfunc_7): an int object id, a byte mode, then the job system block: an int rank, an int job group, a byte
// skill count and per skill (JobSkill::Read, jobsystem.cpp:129) an int id, an int level, and three bools (changed, learned,
// extra). Mode 3 reads a byte error (JobErrorTests).
public class JobLayoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static SkillInfo Info() {
        var jobTable = new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Thief] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [new JobTable.Skill(10700081, [], 10, 11)],
                [SkillRank.Awakening] = [],
            }, []),
        });
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(skills)!;
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)) with { MaxLevel = 10 };
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(10700081, (short) 1), new SkillMetadata(10700081, 1, null, property, null!, null!)]);
        var info = new SkillInfo(jobTable, skills, Job.Thief, null);
        info.Skills.Cast<IDictionary<int, SkillInfo.Skill>>().Single(dict => dict.ContainsKey(10700081))[10700081].SetLevel(3);
        return info;
    }

    [Test]
    public void EveryModeCarriesTheJobSystemBlock() {
        var test = new TestGameSession();
        test.Start();
        var player = test.Session.Player;
        foreach ((Func<ByteWriter> make, byte mode) in new (Func<ByteWriter>, byte)[] {
                     (() => JobPacket.Notify(player, Info()), 0),
                     (() => JobPacket.Basic(player, Info()), 1),
                     (() => JobPacket.Advance(player, Info()), 2),
                     (() => JobPacket.Unknown(player, Info()), 7),
                     (() => JobPacket.Load(player, Info()), 8),
                     (() => JobPacket.Update(player, Info()), 9),
                     (() => JobPacket.Reset(player, Info()), 10),
                     (() => JobPacket.AutoDistribute(player, Info()), 11),
                 }) {
            var reader = new ByteReader(make().ToArray(), 0);
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Job));
            Assert.That(reader.ReadInt(), Is.EqualTo(player.ObjectId));
            Assert.That(reader.ReadByte(), Is.EqualTo(mode));
            Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Thief % 10 + 1)); // rank
            Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Thief.Code())); // job group
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(10700081));
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
            Assert.That(reader.ReadBool(), Is.True); // changed: the level was just set
            Assert.That(reader.ReadBool(), Is.True); // learned
            Assert.That(reader.ReadBool(), Is.False); // extra
            Assert.That(reader.Available, Is.Zero);
        }
    }
}
