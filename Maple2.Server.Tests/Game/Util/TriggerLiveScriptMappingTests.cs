using System;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Utils;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using TriggerNodes = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// The actions and conditions only the live scripts use (02200001, 02200002, 61000035, ...): the ingest snake-cases
// their names and attributes, and the server maps each one.
public class TriggerLiveScriptMappingTests {
    [TestCase("AddAdditionalEffect")]
    [TestCase("LookCenterCameraOnOff")]
    [TestCase("LookCenterCameraRadian")]
    [TestCase("LookCenterCameraSetting")]
    [TestCase("RemoveAdditionalEffect")]
    [TestCase("RemoveNpc")]
    [TestCase("SpawnItemGroup")]
    [TestCase("SpawnNpc")]
    public void Action_IsMapped(string clientName) {
        Assert.That(TriggerFunctionMapping.ActionMap.ContainsKey(TriggerTranslate.ToSnakeCase(clientName)), Is.True);
    }

    [TestCase("CheckMaxUserCount")]
    [TestCase("CheckNpcSlaveCount")]
    [TestCase("CheckUserAdditionalCountInRose")]
    [TestCase("NpcIsDead")]
    [TestCase("Random")]
    public void Condition_IsMapped(string clientName) {
        Assert.That(TriggerFunctionMapping.ConditionMap.ContainsKey(TriggerTranslate.ToSnakeCase(clientName)), Is.True);
    }

    [TestCase("boxID", "box_id")]
    [TestCase("centerPos", "center_pos")]
    [TestCase("outDist", "out_dist")]
    [TestCase("spotLightCount", "spot_light_count")]
    [TestCase("triggerBoxIDs", "trigger_box_ids")]
    [TestCase("spawnPointID", "spawn_point_id")]
    [TestCase("randomPickCount", "random_pick_count")]
    [TestCase("group0", "group0")]
    [TestCase("arg8", "arg8")]
    public void Attribute_SnakeCase(string clientName, string serverName) {
        Assert.That(TriggerTranslate.ToSnakeCase(clientName), Is.EqualTo(serverName));
    }

    // 02200001's "random one of 1~8": the state's random conditions split the roll in equal shares.
    [Test]
    public void Random_EqualSharesOfTheState() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2><state name="pick">
              <condition name="random" arg1="1"><transition state="a" /></condition>
              <condition name="random" arg1="2"><transition state="b" /></condition>
              <condition name="random" arg1="3"><transition state="c" /></condition>
              <condition name="random" arg1="4"><transition state="d" /></condition>
            </state></ms2>
            """);
        TriggerNodes trigger = TriggerCache.ParseTrigger("02200001", "main", document);

        TriggerNodes.State state = trigger.States.Single();
        Assert.That(state.Conditions.Select(condition => (condition.Name, condition.NextState)),
            Is.EqualTo(new[] { ("random_condition", "a"), ("random_condition", "b"), ("random_condition", "c"), ("random_condition", "d") }));
        Assert.That(TriggerFunctionMapping.RandomShare(0, 4), Is.EqualTo(25f));
        Assert.That(TriggerFunctionMapping.RandomShare(3, 4), Is.EqualTo(100f));
        Assert.That(TriggerFunctionMapping.RandomShare(1, 8), Is.EqualTo(12.5f));
        Assert.That(TriggerFunctionMapping.RandomShare(-1, 0), Is.EqualTo(100f));
        // Three outcomes: 100f / 3 twice leaves a roll of 99.99 to the third, not to a re-roll.
        float roll = 99.99f - 2 * TriggerFunctionMapping.RandomShare(0, 3);
        Assert.That(roll - TriggerFunctionMapping.RandomShare(2, 3), Is.LessThan(0f));
    }

    // 61000035's SpawnItemGroup: group_count names group0..groupN-1.
    [Test]
    public void SpawnItemGroup_ReadsEachGroup() {
        var document = new XmlDocument();
        document.LoadXml("""<action name="spawn_item_group" group_count="2" group0="1,2,3" group1="4-6" random_pick_count="2" life_time="20" />""");
        IAction action = TriggerFunctionMapping.ActionMap["spawn_item_group"](document.DocumentElement!.Attributes);
        Assert.That(action, Is.TypeOf<TriggerNodes.SpawnItemGroup>());

        int[] picked = TriggerRandomPick.Pick(new[] { 4, 5, 6 }, 2, new Random(3));
        Assert.That(picked, Has.Length.EqualTo(2));
        Assert.That(picked, Is.Unique);
        Assert.That(picked, Is.SubsetOf(new[] { 4, 5, 6 }));
    }
}
