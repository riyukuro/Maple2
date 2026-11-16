using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// An effect's begin condition on a player caster (BeginCondition.Check), from the KR effect rows by use count. The
// client only parses these fields (AdditionalLevelData's condition, additionalactionadditional.cpp:1580-1586), so
// the server's checks are the rules.
public class BeginConditionRowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldPlayer Enter(TestGameSession test, int mapId = 2000001, MapType type = MapType.None,
            Continent continent = Continent.VictoriaIsland) {
        test.Start();
        MapMetadata map = TestGameSession.Map(mapId);
        test.EnterMap(map with { Property = map.Property with { Type = type, Continent = continent } });
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        return player;
    }

    private static FieldPlayer Enter(int mapId = 2000001, MapType type = MapType.None, Continent continent = Continent.VictoriaIsland) {
        return Enter(new TestGameSession(), mapId, type, continent);
    }

    // The KR defaults: no allowDeadState, no allowOnSurvival, nothing else asked. Battle mounts are allowed here: a
    // test session has no ride manager to look at.
    internal static readonly BeginCondition Plain = new(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, false, [],
        new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);

    private static bool Holds(BeginCondition condition, FieldPlayer player) => condition.Check(player, player, player);

    // 10400281 (KR: allowDeadState) holds for a dead caster; a plain condition does not.
    [Test]
    public void AllowDead_HoldsForADeadCaster() {
        FieldPlayer player = Enter();
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(player, true);

        Assert.That(Holds(Plain, player), Is.False);
        Assert.That(Holds(Plain with { AllowDead = true }, player), Is.True);
    }

    // 90050001 (KR: jobCode 1 and 10, beginner and knight).
    [TestCase(Job.Newbie, true)]
    [TestCase(Job.Knight, true)]
    [TestCase(Job.Berserker, false)]
    public void JobCode_HoldsForTheListedJobs(Job job, bool holds) {
        FieldPlayer player = Enter();
        player.Value.Character.Job = job;

        Assert.That(Holds(Plain with { JobCode = [JobCode.Newbie, JobCode.Knight] }, player), Is.EqualTo(holds));
    }

    // 10100151 (KR: activeSkill 10100151) holds while the holder casts that skill.
    [Test]
    public void ActiveSkill_HoldsWhileTheSkillPlays() {
        FieldPlayer player = Enter();
        // A test session has no animation manager; a bare one of the player holds the playing skill.
        typeof(Actor<Player>).GetField("<Animation>k__BackingField", Any)!.SetValue(player, new AnimationManager((IActor) player));
        BeginCondition condition = Plain with { ActiveSkill = [10100151] };
        Assert.That(Holds(condition, player), Is.False);

        player.Animation.Current = new AnimationRecord(new AnimationSequenceMetadata("Attack_01_A", 1, 1f, []), 1f, Maple2.Server.Game.Model.Enum.AnimationType.Skill, Skill(10100151));
        Assert.That(Holds(condition, player), Is.True);
        player.Animation.Current = new AnimationRecord(new AnimationSequenceMetadata("Attack_01_A", 1, 1f, []), 1f, Maple2.Server.Game.Model.Enum.AnimationType.Skill, Skill(10100161));
        Assert.That(Holds(condition, player), Is.False);
    }

    private static SkillMetadata Skill(int id) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var data = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel));
        return new SkillMetadata(id, 1, null, property, null!, data);
    }

    // 40011001 (KR: allowOnSurvival) holds in a survival map; a plain condition does not.
    [TestCase(MapType.SurvivalSolo)]
    [TestCase(MapType.SurvivalTeam)]
    public void AllowOnSurvival_HoldsInSurvival(MapType type) {
        FieldPlayer player = Enter(type: type);

        Assert.That(Holds(Plain, player), Is.False);
        Assert.That(Holds(Plain with { AllowOnSurvival = true }, player), Is.True);
    }

    // 10100111 (KR: a longsword, item type 32, in the right hand; the left hand of type 0 is anything).
    [Test]
    public void Weapon_HoldsWithTheWeaponInHand() {
        BeginCondition condition = Plain with { Weapon = [new BeginConditionWeapon(new ItemType(1, 0), new ItemType(1, 32))] };
        FieldPlayer bare = Enter();
        Assert.That(Holds(condition, bare), Is.False);

        var test = new TestGameSession();
        test.Store(TestGameSession.Metadata(13200001, EquipSlot.RH, slotMax: 1), group: ItemGroup.Gear, slot: EquipSlot.RH);
        FieldPlayer armed = Enter(test);
        Assert.That(Holds(condition, armed), Is.True);
    }

    // 71000030 (KR: maps 2100001) holds only on that map.
    [Test]
    public void Maps_HoldOnlyOnTheListedMap() {
        BeginCondition condition = Plain with { Maps = [2100001] };

        Assert.That(Holds(condition, Enter(2100001)), Is.True);
        Assert.That(Holds(condition, Enter(2000001)), Is.False);
    }

    // 70000079 (KR: mapCategory 9, the PvP maps).
    [Test]
    public void MapTypes_HoldOnlyOnTheListedType() {
        BeginCondition condition = Plain with { MapTypes = [MapType.Pvp] };

        Assert.That(Holds(condition, Enter(type: MapType.Pvp)), Is.True);
        Assert.That(Holds(condition, Enter(type: MapType.Dungeon)), Is.False);
    }

    // 90051128 (KR: mapContinent 202, the shadow world).
    [Test]
    public void Continents_HoldOnlyOnTheListedContinent() {
        BeginCondition condition = Plain with { Continents = [Continent.ShadowWorld] };

        Assert.That(Holds(condition, Enter(continent: Continent.ShadowWorld)), Is.True);
        Assert.That(Holds(condition, Enter(continent: Continent.VictoriaIsland)), Is.False);
    }

    // 70000027 (KR: onlyShadowWorld) holds only on a map of type Shadow.
    [Test]
    public void OnlyShadowWorld_HoldsOnAShadowMap() {
        BeginCondition condition = Plain with { OnlyShadowWorld = true };

        Assert.That(Holds(condition, Enter(type: MapType.Shadow)), Is.True);
        Assert.That(Holds(condition, Enter()), Is.False);
    }

    // 10200231 (KR: durationWithoutMoving 100) holds once the player has stood still for 100 ms.
    [Test]
    public void DurationWithoutMoving_HoldsAfterStandingStill() {
        FieldPlayer player = Enter();
        BeginCondition condition = Plain with { DurationWithoutMoving = 100 };

        player.PositionTick = (player.Position, 0, 99);
        Assert.That(Holds(condition, player), Is.False);
        player.PositionTick = (player.Position, 0, 100);
        Assert.That(Holds(condition, player), Is.True);
    }

    // requireStates on the owner: 70001111 and 70001241 (KR: gosEmotion, gosEmotionIdle) hold only while the player is
    // in one of those states, 71000030 and 90000626 (gosPlayInstrument) only while playing. The ingest takes the names by
    // the states' descriptions (11, 15 and 47).
    [TestCase(ActorState.Emotion, true)]
    [TestCase(ActorState.EmotionIdle, true)]
    [TestCase(ActorState.Idle, false)]
    [TestCase(ActorState.PlayInstrument, false)]
    public void RequireStates_HoldsOnlyInTheListedStates(ActorState state, bool holds) {
        FieldPlayer player = Enter();
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [],
            [ActorState.Emotion, ActorState.EmotionIdle], [], new Dictionary<MasteryType, int>(), [], []);
        player.State = state;

        Assert.That(Holds(Plain with { Owner = owner }, player), Is.EqualTo(holds));
        Assert.That(Holds(Plain with { Owner = owner with { States = [] } }, player), Is.True, "no state asked");
    }

    // requireSubStates on the owner: 71000030 and 90000626 (KR: StatePlayInstrument_Playing_Score_Solo and _Ensemble,
    // sub states 127 and 129) hold only while the player plays a score, alone or in an ensemble, not while playing
    // freely (126).
    [TestCase(ActorSubState.PlayInstrument_Playing_Score_Solo, true)]
    [TestCase(ActorSubState.PlayInstrument_Playing_Score_Ensemble, true)]
    [TestCase(ActorSubState.PlayInstrument_Playing_Direct, false)]
    [TestCase(ActorSubState.None, false)]
    public void RequireSubStates_HoldsOnlyInTheListedSubStates(ActorSubState subState, bool holds) {
        FieldPlayer player = Enter();
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [],
            [], [ActorSubState.PlayInstrument_Playing_Score_Solo, ActorSubState.PlayInstrument_Playing_Score_Ensemble],
            new Dictionary<MasteryType, int>(), [], []);
        player.SubState = subState;

        Assert.That(Holds(Plain with { Owner = owner }, player), Is.EqualTo(holds));
        Assert.That(Holds(Plain with { Owner = owner with { SubStates = [] } }, player), Is.True, "no sub state asked");
    }

    // requireMasteryTypes / requireMasteryValues on the owner: 90000626 (KR: Music 10800) holds only for a player whose
    // performance mastery is at least 10800; 71000030 (Music 0) for any player.
    [TestCase(10800, 10799, false)]
    [TestCase(10800, 10800, true)]
    [TestCase(10800, 20000, true)]
    [TestCase(0, 0, true)]
    public void RequireMastery_HoldsFromTheValueUp(int required, int mastery, bool holds) {
        FieldPlayer player = Enter();
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [],
            [], [], new Dictionary<MasteryType, int> { [MasteryType.Music] = required }, [], []);
        player.Value.Character.Mastery.Instrument = mastery;

        Assert.That(Holds(Plain with { Owner = owner }, player), Is.EqualTo(holds));
    }

    // The caster's own requirements of a skill's begin condition. 90200032 (KR: gender 1, stat Health 1): a female
    // caster with health holds; a male one, or one whose health total is below 1, does not.
    [TestCase(Gender.Female, 1, true)]
    [TestCase(Gender.Male, 1, false)]
    [TestCase(Gender.Female, 0, false)]
    public void GenderAndStat_AreTheCastersOwn(Gender gender, long health, bool holds) {
        FieldPlayer player = Enter(new TestGameSession(gender: gender));
        Stat stat = player.Stats.Values[BasicAttribute.Health];
        (stat.Total, stat.Current) = (health, health);
        var condition = Plain with { Gender = Gender.Female, Stat = new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1 } };

        Assert.That(Holds(condition, player), Is.EqualTo(holds));
    }

    // onlyFlyableMap (KR: 154 skills, as 80010002): only on a map where flying is allowed (the map's canFly).
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void OnlyFlyableMap_HoldsWhereFlyingIsAllowed(bool canFly, bool holds) {
        var test = new TestGameSession();
        test.Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with { Property = map.Property with { CanFly = canFly } });
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);

        Assert.That(Holds(Plain with { OnlyFlyableMap = true }, player), Is.EqualTo(holds));
        Assert.That(Holds(Plain, player), Is.True);
    }

    // level and mesos: the caster's character level and mesos at least the values (no KR row asks more than level 1
    // or any mesos; the checks are pinned as they are).
    [TestCase(49, 0, false)]
    [TestCase(50, 0, true)]
    [TestCase(50, 999, false)]
    [TestCase(50, 1000, true)]
    public void LevelAndMesos_AreTheCastersAtLeast(short level, long mesos, bool holds) {
        FieldPlayer player = Enter();
        player.Value.Character.Level = level;
        player.Session.Currency.Meso = mesos;
        var condition = Plain with { Level = 50, Mesos = mesos > 0 ? 1000 : 0 };

        Assert.That(Holds(condition, player), Is.EqualTo(holds));
    }

    internal static BeginConditionTarget Side(BeginConditionTarget.BeginConditionStat[]? stat = null, EventConditionType type = EventConditionType.Activate,
            int[]? eventBuffIds = null, int[]? npcIds = null) {
        return new BeginConditionTarget([], new BeginConditionTarget.EventCondition(type, false, [], eventBuffIds ?? [], []), stat ?? [],
            [], [], new Dictionary<MasteryType, int>(), npcIds ?? [], []);
    }

    // ownerStat on health, by the current share of the total (the data's default value type): 10200081 (KR: at least
    // 0.45 and below 0.6) holds in that band, 10100231 (at least 0.7) from 70% up. A total of 0 counts as 0%.
    [TestCase(44, 100, false, false)]
    [TestCase(45, 100, true, false)]
    [TestCase(59, 100, true, false)]
    [TestCase(60, 100, false, false)]
    [TestCase(70, 100, false, true)]
    [TestCase(100, 100, false, true)]
    [TestCase(0, 0, false, false)]
    public void OwnerHealthShare_HoldsInTheRowsBand(long current, long total, bool band, bool high) {
        FieldPlayer player = Enter();
        Stat health = player.Stats.Values[BasicAttribute.Health];
        (health.Total, health.Current) = (total, current);
        BeginCondition mid = Plain with { Owner = Side([
            new BeginConditionTarget.BeginConditionStat(BasicAttribute.Health, 0.45f, CompareType.GreaterEquals, CompareStatValueType.CurrentPercentage),
            new BeginConditionTarget.BeginConditionStat(BasicAttribute.Health, 0.6f, CompareType.Less, CompareStatValueType.CurrentPercentage),
        ]) };
        BeginCondition top = Plain with { Owner = Side([
            new BeginConditionTarget.BeginConditionStat(BasicAttribute.Health, 0.7f, CompareType.GreaterEquals, CompareStatValueType.CurrentPercentage),
        ]) };

        Assert.That(Holds(mid, player), Is.EqualTo(band));
        Assert.That(Holds(top, player), Is.EqualTo(high));
    }

    // A stat compare of Equals, on the total value: no KR row asks one, but the data's compare type allows it.
    [TestCase(500, true)]
    [TestCase(499, false)]
    public void OwnerTotalEquals_HoldsOnlyAtTheValue(long total, bool holds) {
        FieldPlayer player = Enter();
        Stat health = player.Stats.Values[BasicAttribute.Health];
        (health.Total, health.Current) = (total, 1);
        BeginCondition condition = Plain with { Owner = Side([
            new BeginConditionTarget.BeginConditionStat(BasicAttribute.Health, 500, CompareType.Equals, CompareStatValueType.TotalValue),
        ]) };

        Assert.That(Holds(condition, player), Is.EqualTo(holds));
    }

    // 10200261 (KR: ownerEventType 16, an effect applied to the holder, eventEffectID 10200033) fires only for that
    // effect, and only on that event.
    [TestCase(EventConditionType.OnEffectApplied, 10200033, true)]
    [TestCase(EventConditionType.OnEffectApplied, 10200034, false)]
    [TestCase(EventConditionType.Activate, 10200033, false)]
    public void EventBuffIds_HoldOnlyForTheNamedEffect(EventConditionType eventType, int buffId, bool holds) {
        FieldPlayer player = Enter();
        BeginCondition condition = Plain with { Owner = Side(type: EventConditionType.OnEffectApplied, eventBuffIds: [10200033]) };

        Assert.That(condition.Check(player, player, player, eventType, eventBuffId: buffId), Is.EqualTo(holds));
    }
}

