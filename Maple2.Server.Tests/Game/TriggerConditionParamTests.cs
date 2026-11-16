using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Condition parameters the other trigger tests leave at their default value: each condition is written as the KR
// scripts write it, run through the ingest's name and parameter mapping, then read by the server's condition map, so
// a parameter read under the wrong key, or dropped by the ingest, changes the answer.
public class TriggerConditionParamTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string ObjectInteracted = "\uC624\uBE0C\uC81D\uD2B8\uAC00\uBC18\uC751\uD588\uC73C\uBA74";
    private const string QuestUserDetected = "\uD018\uC2A4\uD2B8\uC720\uC800\uB97C\uAC10\uC9C0\uD558\uBA74";
    private const string MonsterDead = "\uBAAC\uC2A4\uD130\uAC00\uC8FD\uC5B4\uC788\uC73C\uBA74";

    private static readonly Ms2TriggerBox Near = new(Vector3.Zero, new Vector3(300, 300, 300), 1, true);
    private static readonly Ms2TriggerBox Far = new(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true);

    // The condition as the ingest writes it: <condition name="..." .../> inside a state of a one-state script.
    private static XmlAttributeCollection Ingested(string clientCondition) {
        var document = new XmlDocument();
        document.LoadXml("<ms2><state name=\"s\">" + clientCondition + "</state></ms2>");
        TriggerMapper.NormalizeTriggerXmlNames(document);
        return document.SelectSingleNode("//condition")!.Attributes!;
    }

    private bool ClientCondition(string clientCondition) {
        XmlAttributeCollection attributes = Ingested(clientCondition);
        return TriggerFunctionMapping.ConditionMap[attributes["name"]!.Value](attributes).Evaluate(context);
    }

    private void StartWithBoxes() {
        Start(Near, Far);
        test.Session.Player.Position = Vector3.Zero;
        foreach (string name in new[] { "fieldPortals", "fieldBreakables", "fieldLiftables", "fieldSkills" }) {
            FieldInfo info = typeof(FieldManager).GetField(name, Any)!;
            info.SetValue(Field, Activator.CreateInstance(info.FieldType));
        }
        typeof(FieldManager).GetField("triggerBreakable", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldBreakable>());
    }

    private FieldInteract AddInteract(int id) {
        var row = new InteractObjectMetadata(id, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
        return test.AddInteract(row, new InteractMeshObject("m" + id, new Ms2InteractMesh(id, Vector3.Zero, Vector3.Zero)));
    }

    // object_interacted arg2 is the state the object must be in: 0 normal (used), 1 reactable, 2 hidden. Of 6766 KR
    // uses, 76 wait for 1 and 66 for 2; 9 write the state as "ar2" (84000006_wd), which the ingest reads as arg2.
    [Test]
    public void ObjectInteracted_State() {
        Start();
        FieldInteract statue = AddInteract(10001);
        Assert.That(statue.State, Is.EqualTo(InteractState.Reactable));

        Assert.That(Ingested($"""<condition name="{ObjectInteracted}" arg1="10001" arg2="1" />""")["state"]?.Value, Is.EqualTo("1"));
        Assert.That(ClientCondition($"""<condition name="{ObjectInteracted}" arg1="10001" arg2="1" />"""), Is.True);
        Assert.That(ClientCondition($"""<condition name="{ObjectInteracted}" arg1="10001" arg2="0" />"""), Is.False);
        Assert.That(ClientCondition($"""<condition name="{ObjectInteracted}" arg1="10001" ar2="2" />"""), Is.False);

        Action("""<action name="set_interact_object" trigger_ids="10001" state="2" />""");
        Assert.That(statue.State, Is.EqualTo(InteractState.Hidden));
        Assert.That(ClientCondition($"""<condition name="{ObjectInteracted}" arg1="10001" ar2="2" />"""), Is.True);
        Assert.That(ClientCondition($"""<condition name="{ObjectInteracted}" arg1="10001" arg2="1" />"""), Is.False);
    }

    private void Action(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlAttributeCollection attributes = document.DocumentElement!.Attributes;
        TriggerFunctionMapping.ActionMap[attributes["name"]!.Value](attributes).Execute(context);
    }

    // quest_user_detected arg4 is a job code (84 KR uses, 10 to 90: the lookinto_<job> scripts): only a player
    // of that job counts; no arg4 is any job.
    [Test]
    public void QuestUserDetected_JobCode() {
        StartWithBoxes();
        test.SetQuest(100, QuestState.Completed);
        test.Session.Player.Value.Character.Job = Job.Knight;

        Assert.That(Ingested($"""<condition name="{QuestUserDetected}" arg1="1" arg2="100" arg3="3" arg4="10" />""")["job_code"]?.Value, Is.EqualTo("10"));
        Assert.That(ClientCondition($"""<condition name="{QuestUserDetected}" arg1="1" arg2="100" arg3="3" arg4="10" />"""), Is.True);
        Assert.That(ClientCondition($"""<condition name="{QuestUserDetected}" arg1="1" arg2="100" arg3="3" arg4="20" />"""), Is.False);
        Assert.That(ClientCondition($"""<condition name="{QuestUserDetected}" arg1="1" arg2="100" arg3="3" />"""), Is.True);
        Assert.That(ClientCondition($"""<condition name="!{QuestUserDetected}" arg1="1" arg2="100" arg3="3" arg4="20" />"""), Is.True);
    }

    // check_any_user_additional_effect level: the effect must be held at that level or above (every KR use says 1).
    [Test]
    public void CheckAnyUserAdditionalEffect_Level() {
        StartWithBoxes();
        WithEffect(70000107);
        test.Session.Player.Buffs.AddBuff(Field.FieldActor, test.Session.Player, 70000107, 1, Field.FieldTick);

        Assert.That(ClientCondition("""<condition name="CheckAnyUserAdditionalEffect" triggerBoxID="1" additionalEffectID="70000107" level="1" />"""), Is.True);
        Assert.That(ClientCondition("""<condition name="CheckAnyUserAdditionalEffect" triggerBoxID="1" additionalEffectID="70000107" level="2" />"""), Is.False);
    }

    // CheckUserAdditionalCountInRose (02200002) level: only holders of the effect at that level fill a spotlight.
    [Test]
    public void CheckUserAdditionalCountInRose_Level() {
        StartWithBoxes();
        WithEffect(70000107);
        test.Session.Player.Buffs.AddBuff(Field.FieldActor, test.Session.Player, 70000107, 1, Field.FieldTick);

        Assert.That(ClientCondition("""<condition name="CheckUserAdditionalCountInRose" triggerBoxIDs="1,2" additionalEffectID="70000107" level="1" spotLightCount="1" />"""), Is.True);
        Assert.That(ClientCondition("""<condition name="CheckUserAdditionalCountInRose" triggerBoxIDs="1,2" additionalEffectID="70000107" level="2" spotLightCount="1" />"""), Is.False);
    }

    // WaitSecondsUserValue key (35 KR uses, 34 of them TimeEventLifeTime): the seconds come from that user value.
    // The state started 5 s ago: 60 s set under the key has not run out, while an unset key (0 s) has.
    [Test]
    public void WaitSecondsUserValue_Key() {
        Start();
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!.SetValue(Field, new TriggerUserValues());
        Field.UserValues.Set(1, "TimeEventLifeTime", 60);
        context.StartTick = Environment.TickCount64 - 5_000;

        Assert.That(ClientCondition("""<condition name="WaitSecondsUserValue" key="TimeEventLifeTime" />"""), Is.False);
        Assert.That(ClientCondition("""<condition name="WaitSecondsUserValue" key="RegenSpeed" />"""), Is.True);
        context.StartTick = Environment.TickCount64 - 61_000;
        Assert.That(ClientCondition("""<condition name="WaitSecondsUserValue" key="TimeEventLifeTime" />"""), Is.True);
    }

    // monster_dead arg2 (autoTarget) reaches the server as auto_target; the server reads it, but no answer depends on it.
    [Test]
    public void MonsterDead_AutoTargetIsMapped() {
        XmlAttributeCollection attributes = Ingested($"""<condition name="{MonsterDead}" arg1="5" arg2="0" />""");
        Assert.That((attributes["name"]!.Value, attributes["spawn_ids"]?.Value, attributes["auto_target"]?.Value), Is.EqualTo(("monster_dead", "5", "0")));
    }

    // The client spellings the server's second keys stand for: the ingest snake-cases them, so the first key is read
    // (CheckNpcHp compare, 99 KR uses; WeddingHallState hallState, 48; CheckNpcDamage damageRate, 2).
    [Test]
    public void ClientSpellings_ReachTheFirstKey() {
        XmlAttributeCollection hp = Ingested("""<condition name="CheckNpcHp" compare="lower" value="50" spawnPointId="101" isRelative="1" />""");
        Assert.That((hp["name"]!.Value, hp["compare"]?.Value, hp["spawn_id"]?.Value, hp["is_relative"]?.Value), Is.EqualTo(("npc_hp", "lower", "101", "1")));
        XmlAttributeCollection hall = Ingested("""<condition name="WeddingHallState" hallState="weddingComplete" success="1" />""");
        Assert.That((hall["name"]!.Value, hall["hall_state"]?.Value), Is.EqualTo(("wedding_hall_state", "weddingComplete")));
        XmlAttributeCollection damage = Ingested("""<condition name="CheckNpcDamage" spawnPointID="101" damageRate="0.5" operator="GreaterEqual" />""");
        Assert.That((damage["name"]!.Value, damage["damage_rate"]?.Value, damage["spawn_id"]?.Value), Is.EqualTo(("npc_damage", "0.5", "101")));
    }
}
