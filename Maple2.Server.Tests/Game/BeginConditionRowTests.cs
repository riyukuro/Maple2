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
    private static readonly BeginCondition Plain = new(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, false, [],
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
}
