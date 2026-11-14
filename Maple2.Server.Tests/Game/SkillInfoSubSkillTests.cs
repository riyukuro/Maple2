using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Config;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// A job table sub skill without a skill file, as SkillInfo.SetJob builds a character's tree at each login.
public class SkillInfoSubSkillTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // KR job.xml, Thief (code 070) rows JobChange_01/02: <skill main="10700081" sub="10700082" uiPosition="3, 4"
    // maxLevel="10" quickSlotPriority="11" />. Haste 10700081 has a skill file; 10700082 is only an additional effect.
    // Two logins: Haste is in the tree without a sub each time, and the missing sub is noted (warned of) once.
    [Test]
    public void AMissingSubSkillIsSkippedAndWarnedOfOnce() {
        var jobTable = new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Thief] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [new JobTable.Skill(10700081, [10700082], 10, 11)],
                [SkillRank.Awakening] = [],
            }, []),
        });
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(skills)!;
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)) with { MaxLevel = 10 };
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(10700081, (short) 1), new SkillMetadata(10700081, 1, null, property, null!, null!)]);

        for (int login = 0; login < 2; login++) {
            var info = new SkillInfo(jobTable, skills, Job.Thief, null);
            SkillInfo.Skill haste = info.Skills.Cast<IDictionary<int, SkillInfo.Skill>>().Single(dict => dict.ContainsKey(10700081))[10700081];
            Assert.Multiple(() => {
                Assert.That((SkillInfo.Skill[]) typeof(SkillInfo.Skill).GetField("subSkills", Any)!.GetValue(haste)!, Is.Empty);
                Assert.That(info.SubSkills.Cast<IDictionary<int, SkillInfo.Skill>>().All(dict => dict.Count == 0), Is.True);
                Assert.That(SkillInfo.MissingSubSkills.ContainsKey(10700082), Is.True);
            });
        }
        // Noted once: a later login does not warn again.
        Assert.That(SkillInfo.MissingSubSkills.TryAdd(10700082, 0), Is.False);
    }
}
