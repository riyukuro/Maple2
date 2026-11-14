using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The RainbowMonster widget of 02100002 (01_maincontrol), with the script's own parameters.
public class RainbowMonsterScoreTests {
    private const int Furnace = 34000141;

    private static RainbowMonsterScore Script(int seed = 1, long now = 0) {
        var score = new RainbowMonsterScore(new Random(seed));
        score.CreateGroup(1, "34000122,34000123,34000124,34000142");
        score.CreateGroup(2, "34000128,34000129,34000130,34000143");
        score.SetKillerNpc("34000141");
        score.SetLoseScoreTick(60000, now);
        score.SetBadNpc("34000121,34000137");
        score.SetBadNpcScore("2-5");
        score.SetBadNpcSoundKey("GuildRaid_RainbowSlimeFactory_ScreenWarning_01");
        score.InitRandomMaxScore("120,120,140,140,160,160");
        score.InitScoreMesh(1, 3100, 24);
        score.InitScoreMesh(2, 3200, 24);
        return score;
    }

    // A group's npc the killer npc kills adds 1 to that group only.
    [Test]
    public void KillerKillScoresTheGroup() {
        RainbowMonsterScore score = Script();
        Assert.That(score.NpcDied(34000142, Furnace, out bool bad), Is.EqualTo(new[] { 1 }));
        Assert.Multiple(() => {
            Assert.That(bad, Is.False);
            Assert.That(score.Score(1), Is.EqualTo(1));
            Assert.That(score.Score(2), Is.Zero);
        });
    }

    // Killed by anything else (a player, another npc), a group's npc scores nothing.
    [Test]
    public void OtherKillerScoresNothing() {
        RainbowMonsterScore score = Script();
        Assert.That(score.NpcDied(34000122, 34000122, out _), Is.Empty);
        Assert.That(score.Score(1), Is.Zero);
    }

    // A bad npc the killer kills takes 2 to 5 from every group, never below 0.
    [Test]
    public void BadNpcTakesFromEveryGroup() {
        RainbowMonsterScore score = Script();
        for (int i = 0; i < 10; i++) {
            score.NpcDied(34000122, Furnace, out _);
        }
        score.NpcDied(34000128, Furnace, out _);
        score.NpcDied(34000121, Furnace, out bool bad);
        Assert.Multiple(() => {
            Assert.That(bad, Is.True);
            Assert.That(score.Score(1), Is.InRange(5, 8));
            Assert.That(score.Score(2), Is.Zero);
        });
    }

    // Every lose tick each group loses 1; ticks missed by a late update all count; 0 is the floor.
    [Test]
    public void LoseScoreTick() {
        RainbowMonsterScore score = Script();
        for (int i = 0; i < 3; i++) {
            score.NpcDied(34000123, Furnace, out _);
        }
        Assert.That(score.Update(59999), Is.Empty);
        Assert.That(score.Update(60000), Is.EqualTo(new[] { 1 }));
        Assert.That(score.Score(1), Is.EqualTo(2));
        score.Update(240000);
        Assert.That(score.Score(1), Is.Zero);
    }

    // Each created group draws one of the listed max scores, without replacement.
    [Test]
    public void RandomMaxScoreWithoutReplacement() {
        for (int seed = 0; seed < 20; seed++) {
            var score = new RainbowMonsterScore(new Random(seed));
            for (int group = 1; group <= 5; group++) {
                score.CreateGroup(group, $"{group}");
            }
            score.InitRandomMaxScore("120,120,140,140,160,160");
            List<int> drawn = Enumerable.Range(1, 5).Select(score.MaxScore).ToList();
            var pool = new List<int> { 120, 120, 140, 140, 160, 160 };
            foreach (int max in drawn) {
                Assert.That(pool.Remove(max), Is.True, $"seed {seed}: {string.Join(",", drawn)}");
            }
        }
    }

    // The lit meshes: score * meshes / max, at most every mesh.
    [Test]
    public void LitMeshes() {
        RainbowMonsterScore score = Script();
        int max = score.MaxScore(1);
        for (int i = 0; i < max / 2; i++) {
            score.NpcDied(34000124, Furnace, out _);
        }
        Assert.That(score.Meshes(1), Is.EqualTo((3100, 24, 12)));
        for (int i = 0; i < max; i++) {
            score.NpcDied(34000124, Furnace, out _);
        }
        Assert.That(score.Meshes(1).Lit, Is.EqualTo(24));
    }

    // IsMissionSuccess "19-21": every group's lit count in the range.
    [Test]
    public void MissionSuccessRange() {
        RainbowMonsterScore score = Script();
        Fill(score, 1, 34000122, 20);
        Assert.That(score.IsMissionSuccess("19-21"), Is.False);
        Fill(score, 2, 34000128, 19);
        Assert.That(score.IsMissionSuccess("19-21"), Is.True);
        Fill(score, 2, 34000128, 24);
        Assert.That(score.IsMissionSuccess("19-21"), Is.False);
    }

    private static void Fill(RainbowMonsterScore score, int group, int npcId, int lit) {
        while (score.Meshes(group).Lit < lit) {
            score.NpcDied(npcId, Furnace, out _);
        }
    }
}

// The widget on a trigger field: create_widget makes it, the score meshes follow the score, widget_value reads it.
public class RainbowMonsterWidgetTests : TriggerTestBase {
    [Test]
    public void ScriptLightsTheScoreMeshes() {
        Start(Enumerable.Range(3100, 24).Select(id => (Ms2Trigger) new Ms2TriggerMesh(1f, id, false, false)).ToArray());
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(Field, new Dictionary<string, Widget>());

        Action("""<action name="create_widget" type="RainbowMonster" />""");
        Assert.That(Field.Widgets["RainbowMonster"], Is.TypeOf<RainbowMonsterWidget>());
        Action("""<action name="widget_action" type="RainbowMonster" func="CreateGroup" widget_arg_num="1" widget_arg="34000122,34000142" />""");
        Action("""<action name="widget_action" type="RainbowMonster" func="SetKillerNpc" widget_arg="34000141" />""");
        Action("""<action name="widget_action" type="RainbowMonster" func="InitRandomMaxScore" widget_arg="24" />""");
        Action("""<action name="widget_action" type="RainbowMonster" func="InitScoreMesh" widget_arg_num="1" widget_arg="3100" />""");
        test.TakeSent();

        var widget = (RainbowMonsterWidget) Field.Widgets["RainbowMonster"];
        widget.NpcDied(34000142, 34000141);
        widget.NpcDied(34000122, 34000141);
        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (3100, true), (3101, true) }));
        Assert.That(Field.TriggerObjects.Meshes[3102].Visible, Is.False);

        for (int i = 0; i < 18; i++) {
            widget.NpcDied(34000122, 34000141);
        }
        Assert.That(Condition("""<condition name="widget_value" type="RainbowMonster" widget_name="IsMissionSuccess" condition="19-21" />"""), Is.True);
        Assert.That(Condition("""<condition name="widget_value" type="RainbowMonster" widget_name="IsMissionSuccess" condition="19-21" negate="1" />"""), Is.False);
    }
}
