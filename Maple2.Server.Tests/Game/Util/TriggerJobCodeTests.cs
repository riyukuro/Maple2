using System;
using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// quest_user_detected job_code: only a player of that job counts; 0 counts every player.
public class TriggerJobCodeTests {
    [TestCase(JobCode.Knight, 0, true)]
    [TestCase(JobCode.SoulBinder, 0, true)]
    [TestCase(JobCode.Knight, 10, true)]
    [TestCase(JobCode.Newbie, 1, true)]
    [TestCase(JobCode.Wizard, 10, false)]
    [TestCase(JobCode.Knight, 1, false)]
    public void Matches(JobCode playerJob, int jobCode, bool expected) {
        Assert.That(TriggerJobCode.Matches(playerJob, jobCode), Is.EqualTo(expected));
    }

    // 52000188 checks the same quest and state once per job, Knight first: each job matches its own branch only.
    [Test]
    public void EachJobTakesItsOwnBranch() {
        int[] branches = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110];
        foreach (int job in branches) {
            int first = Array.FindIndex(branches, code => TriggerJobCode.Matches((JobCode) job, code));
            Assert.That(branches[first], Is.EqualTo(job));
        }
    }
}
