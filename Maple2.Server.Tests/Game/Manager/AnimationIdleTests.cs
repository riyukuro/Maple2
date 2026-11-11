using System.Collections.Generic;
using System.Linq;
﻿using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game.Manager;

// The idle sequence of a spawned npc, picked from its rig by the npc's first normal action.
public class AnimationIdleTests {
    private static Dictionary<string, AnimationSequenceMetadata> Rig(params (string Name, short Id)[] sequences) =>
        sequences.ToDictionary(s => s.Name, s => new AnimationSequenceMetadata(s.Name, s.Id, 1f, []));

    [Test]
    public void ActionInAnotherCase_FindsTheRigSequence() {
        // npc 21420518: normal action "down_Idle_A", rig 11000751_Dwarf1 "Down_Idle_A".
        var rig = Rig(("Idle_A", 1), ("Down_Idle_A", 2), ("Talk_A", 3), ("Walk_A", 4));
        Assert.That(AnimationManager.FindIdleSequenceId(rig, "down_Idle_A"), Is.EqualTo(2));
    }

    [Test]
    public void ExactName_BeatsALongerNameHoldingIt() {
        var rig = Rig(("Attack_Idle_A", 5), ("Idle_A", 1));
        Assert.That(AnimationManager.FindIdleSequenceId(rig, "Idle_A"), Is.EqualTo(1));
    }

    [Test]
    public void PartOfAName_StillMatches() {
        var rig = Rig(("Idle_A", 1), ("Regen_Idle_B", 7));
        Assert.That(AnimationManager.FindIdleSequenceId(rig, "Idle_B"), Is.EqualTo(7));
    }

    [Test]
    public void MissingAction_FallsBackToIdleA() {
        // npc 21420525: "Down_Idle_A", rig 11000853_Sw_Man1 only has "DownIdle_A".
        var rig = Rig(("Bore_A", 3), ("Idle_A", 1), ("DownIdle_A", 2));
        Assert.That(AnimationManager.FindIdleSequenceId(rig, "Down_Idle_A"), Is.EqualTo(1));
        Assert.That(AnimationManager.FindIdleSequenceId(rig, null), Is.EqualTo(1));
    }

    [Test]
    public void NoMatchAndNoIdleA_IsZero() {
        var rig = Rig(("Attack_01_A", 3));
        Assert.That(AnimationManager.FindIdleSequenceId(rig, "Sit_Down_A"), Is.EqualTo(0));
        Assert.That(AnimationManager.FindIdleSequenceId(Rig(), null), Is.EqualTo(0));
    }
}
