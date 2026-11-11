using System.Xml;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerActions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// The Dark Stream run rules the shipped scripts give. Made-up ids.
public class DarkStreamGameTests {
    [Test]
    public void ClearedSpawn_GivesItsScoreOnce() {
        var game = new DarkStreamGame(3);
        game.SetSpawnScore(901001, 600);
        Assert.That(game.SpawnCleared(901001), Is.True);
        Assert.That(game.Score, Is.EqualTo(600));
        Assert.That(game.SpawnCleared(901001), Is.False);
        Assert.That(game.Score, Is.EqualTo(600));
    }

    [Test]
    public void SpawnWithoutScore_GivesNothing() {
        var game = new DarkStreamGame(3);
        Assert.That(game.SpawnCleared(901002), Is.False);
        Assert.That(game.Score, Is.EqualTo(0));
    }

    [Test]
    public void SpawnedAgain_GivesTheScoreAgain() {
        // A time-attack script spawns the same spawn point again after it is cleared.
        var game = new DarkStreamGame(3);
        game.SetSpawnScore(901003, 410);
        game.SpawnCleared(901003);
        game.SetSpawnScore(901003, 410);
        game.SpawnCleared(901003);
        Assert.That(game.Score, Is.EqualTo(820));
    }

    [Test]
    public void LastRound_EndsTheRunOnce() {
        var game = new DarkStreamGame(3);
        Assert.That(game.ClearRound(1), Is.False);
        Assert.That(game.ClearRound(2), Is.False);
        Assert.That(game.Ended, Is.False);
        Assert.That(game.ClearRound(3), Is.True);
        Assert.That(game.Ended, Is.True);
        Assert.That(game.ClearRound(3), Is.False);
    }

    [Test]
    public void EndedRun_TakesNoMoreScore() {
        var game = new DarkStreamGame(1);
        game.SetSpawnScore(901004, 500);
        game.ClearRound(1);
        Assert.That(game.SpawnCleared(901004), Is.False);
        Assert.That(game.Score, Is.EqualTo(0));
    }

    [Test]
    public void DamagePenalty_IsKeptNotApplied() {
        var game = new DarkStreamGame(3);
        game.SetSpawnScore(901005, 700);
        game.StartRound(2, 15);
        game.SpawnCleared(901005);
        Assert.That(game.Round, Is.EqualTo(2));
        Assert.That(game.DamagePenalty, Is.EqualTo(15));
        Assert.That(game.Score, Is.EqualTo(700));
    }

    [TestCase("""<action name="dark_stream_start_game" round="3" />""", typeof(TriggerActions.DarkStreamStartGame))]
    [TestCase("""<action name="dark_stream_start_round" round="1" ui_duration="3000" damage_penalty="5" />""", typeof(TriggerActions.DarkStreamStartRound))]
    [TestCase("""<action name="dark_stream_spawn_monster" spawn_ids="901001" score="600" />""", typeof(TriggerActions.DarkStreamSpawnMonster))]
    [TestCase("""<action name="dark_stream_clear_round" round="1" />""", typeof(TriggerActions.DarkStreamClearRound))]
    public void Actions_Parse(string xml, System.Type type) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        Assert.That(TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes), Is.TypeOf(type));
    }
}
