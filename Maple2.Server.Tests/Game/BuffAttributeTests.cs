using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// Additional effect attributes as the player's BuffManager applies them, each on the values of a KR additionaleffect
// row (the effect id is named at each case).
public class BuffAttributeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldManager Field => test.Session.Field!;
    private FieldPlayer Player => test.Session.Player;
    private BuffManager Buffs => test.Session.Player.Buffs;

    // The property of a plain buff: 10 s, one stack, kept nowhere special.
    private static readonly AdditionalEffectMetadataProperty Plain = DisabledBuffTests.Property with { DurationTick = 10000 };

    // A session in a field of the given type that knows the given effects (level 1).
    private void Start(MapType type, params AdditionalEffectMetadata[] effects) {
        test = new TestGameSession();
        test.Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with { Property = map.Property with { Type = type } });
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (AdditionalEffectMetadata effect in effects) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(effect.Id, effect.Level), effect]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(Field, new FieldActor(Field, 99, Field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, Field);
        test.TakeSent();
    }

    // The effect with the given property and no begin condition, update, reflect or consume.
    private static AdditionalEffectMetadata Effect(int id, AdditionalEffectMetadataProperty property) {
        var always = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);
        return DisabledBuffTests.Effect(id, property: property) with {
            Condition = always,
            Reflect = new AdditionalEffectMetadataReflect(0, 0, 0, 0, 0, 0, new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>()),
            Update = new AdditionalEffectMetadataUpdate(null, [], [], [], []),
            Consume = new AdditionalEffectMetadataConsume(0, 0),
        };
    }

    private void Add(int id, long tick = 1000, short level = 1, IActor? caster = null, EventConditionType type = EventConditionType.Activate) =>
        Buffs.AddBuff(caster ?? Player, Player, id, level, tick, type: type);

    private Buff Only(int id) => Buffs.EnumerateBuffs(id).Single();

    private int[] Held() => Buffs.EnumerateBuffs().Select(buff => buff.Id).OrderBy(id => id).ToArray();

    private void EnterField() => typeof(BuffManager).GetMethod("EnterField", Any)!.Invoke(Buffs, []);

    // clearEffectFromPVPZone and doNotClearEffectFromEnterPVPZone: entering a PvP map takes off every buff that does
    // not keep itself there, and every buff that clears there even if it keeps. 10000061 keeps (doNot only);
    // 10200141 has both, so it goes; 10000051 has neither, so it goes.
    [TestCase(MapType.Pvp, new[] { 10000061 })]
    [TestCase(MapType.None, new[] { 10000051, 10000061, 10200141 })]
    [TestCase(MapType.Dungeon, new[] { 10000051, 10000061, 10200141 })]
    public void PvpZoneEntry_ClearsByTheTwoFlags(MapType type, int[] kept) {
        Start(type,
            Effect(10000061, Plain with { KeepOnEnterPvpZone = true }),
            Effect(10200141, Plain with { KeepOnEnterPvpZone = true, RemoveOnPvpZone = true }),
            Effect(10000051, Plain));
        Add(10000061);
        Add(10200141);
        Add(10000051);

        EnterField();

        Assert.That(Held(), Is.EqualTo(kept));
    }

    // deadKeepEffect: a death takes off every buff without it. 10000061 (KR: deadKeepEffect 1), 10000051 (not set).
    [Test]
    public void Death_KeepsOnlyDeadKeepEffects() {
        Start(MapType.None, Effect(10000061, Plain with { KeepOnDeath = true }), Effect(10000051, Plain));
        Add(10000061);
        Add(10000051);

        Buffs.OnDeath();

        Assert.That(Held(), Is.EqualTo(new[] { 10000061 }));
    }

    // logoutClearEffect: the buffs saved for the next login leave out the ones that clear at logout. 10500171 (KR:
    // logoutClearEffect 1, 15 s), 10000051 (not set).
    [Test]
    public void Logout_SavesOnlyTheBuffsThatStay() {
        Start(MapType.None, Effect(10500171, Plain with { DurationTick = 15000, RemoveOnLogout = true }), Effect(10000051, Plain));
        Add(10500171);
        Add(10000051);

        Assert.That(Buffs.GetSaveCacheBuffs().Select(buff => buff.Id), Is.EqualTo(new[] { 10000051 }));
    }

    // removeOnLeaveField: leaving a field takes the buff off. 40011011 (KR: removeOnLeaveField 1, 10 s).
    [Test]
    public void LeaveField_TakesOffRemoveOnLeaveField() {
        Start(MapType.None, Effect(40011011, Plain with { RemoveOnLeaveField = true }), Effect(10000051, Plain));
        Add(40011011);
        Add(10000051);

        Buffs.LeaveField();

        Assert.That(Held(), Is.EqualTo(new[] { 10000051 }));
    }

    // delayTick: the first proc waits the delay. 10200033 (KR: delayTick 100, intervalTick 1000, 1 s).
    [Test]
    public void DelayTick_PutsOffTheFirstProc() {
        Start(MapType.None, Effect(10200033, Plain with { DurationTick = 1000, IntervalTick = 1000, DelayTick = 100 }));
        Add(10200033, tick: 1000);

        Assert.That(Only(10200033).NextProcTick, Is.EqualTo(1100));
    }

    // group: a buff takes off the held buffs of its group. 50000324 and 50000330 (KR: group 5, 210 s and 11 s).
    [Test]
    public void Group_TakesOffTheGroupsOtherBuff() {
        Start(MapType.None,
            Effect(50000324, Plain with { Group = 5, DurationTick = 210000, DelayTick = 1000 }),
            Effect(50000330, Plain with { Group = 5, DurationTick = 11000 }),
            Effect(10000051, Plain));
        Add(50000324);
        Add(10000051);
        Add(50000330);

        Assert.That(Held(), Is.EqualTo(new[] { 10000051, 50000330 }));
    }

    // casterIndividualEffect: each caster's buff is its own. 10200112 (KR: casterIndividualEffect 1, 10 s); 10000051
    // (not set) is one buff whoever casts it.
    [TestCase(10200112, 2)]
    [TestCase(10000051, 1)]
    public void CasterIndividual_HoldsOneBuffPerCaster(int id, int count) {
        Start(MapType.None, Effect(10200112, Plain with { IntervalTick = 1000, CasterIndividualBuff = true }), Effect(10000051, Plain));
        Add(id);
        Add(id, caster: Field.FieldActor);

        Assert.That(Buffs.EnumerateBuffs(id), Has.Count.EqualTo(count));
    }

    // coolDownTime: a buff an event gives comes again only after its cooldown; one an Activate gives always comes.
    // 10200032 (KR: coolDownTime 1000, maxBuffCount 10, resetCondition 2, 7 s).
    [TestCase(EventConditionType.OnSkillCasted, 1500, 1)]
    [TestCase(EventConditionType.OnSkillCasted, 2000, 2)]
    [TestCase(EventConditionType.Activate, 1500, 2)]
    public void Cooldown_HoldsOffAnEventsBuff(EventConditionType type, long again, int stacks) {
        Start(MapType.None, Effect(10200032, Plain with { DurationTick = 7000, MaxCount = 10, CooldownTime = 1000, ResetCondition = BuffResetCondition.Reset2 }));
        Add(10200032, tick: 1000, type: type);
        Add(10200032, tick: again, type: type);

        Assert.That(Only(10200032).Stacks, Is.EqualTo(stacks));
    }

    // maxBuffCount: each application adds a stack up to the maximum. 10200232 (KR: maxBuffCount 3, 5 s).
    [Test]
    public void MaxCount_CapsTheStacks() {
        Start(MapType.None, Effect(10200232, Plain with { DurationTick = 5000, MaxCount = 3, ResetCondition = BuffResetCondition.Reset2 }));
        for (int i = 0; i < 5; i++) {
            Add(10200232, tick: 1000 + i);
        }

        Assert.That(Only(10200232).Stacks, Is.EqualTo(3));
    }

    // resetCondition 1: a second application keeps the end. 10200253 (KR: resetCondition 1, 1 s).
    [Test]
    public void PersistEndTick_KeepsTheEnd() {
        Start(MapType.None, Effect(10200253, Plain with { DurationTick = 1000, IntervalTick = 1000, ResetCondition = BuffResetCondition.PersistEndTick }));
        Add(10200253, tick: 1000);
        Add(10200253, tick: 1500);

        Assert.That(Only(10200253).EndTick, Is.EqualTo(2000));
    }

    // resetCondition 3: a second application replaces the buff, so its stacks start over. 10200051 (KR:
    // resetCondition 3, maxBuffCount 2, 1 s).
    [Test]
    public void Replace_StartsANewBuff() {
        Start(MapType.None, Effect(10200051, Plain with { DurationTick = 1000, MaxCount = 2, ResetCondition = BuffResetCondition.Replace }));
        Add(10200051, tick: 1000);
        int first = Only(10200051).ObjectId;
        Add(10200051, tick: 1500);

        Buff buff = Only(10200051);
        Assert.Multiple(() => {
            Assert.That(buff.ObjectId, Is.Not.EqualTo(first));
            Assert.That(buff.Stacks, Is.EqualTo(1));
            Assert.That(buff.StartTick, Is.EqualTo(1500));
            Assert.That(buff.EndTick, Is.EqualTo(2500));
        });
    }

    // keepCondition 99: a buff without a duration stays past its end. 10100061 (KR: keepCondition 99, no durationTick).
    [Test]
    public void UnlimitedDuration_Stays() {
        Start(MapType.None, Effect(10100061, Plain with { DurationTick = 0, KeepCondition = BuffKeepCondition.UnlimitedDuration }));
        Add(10100061, tick: 1000);

        Buffs.Update(100000);
        Buffs.Update(100001);

        Assert.That(Held(), Is.EqualTo(new[] { 10100061 }));
    }

    // A higher level replaces the held buff; a lower one stacks on the held level. 10200032 (KR: maxBuffCount 10).
    [TestCase((short) 2, (short) 2, 1)]
    [TestCase((short) 1, (short) 2, 2)]
    public void Level_HigherReplaces(short second, short level, int stacks) {
        AdditionalEffectMetadata effect = Effect(10200032, Plain with { DurationTick = 7000, MaxCount = 10, ResetCondition = BuffResetCondition.Reset2 });
        Start(MapType.None, effect, effect with { Level = 2 });
        Add(10200032, tick: 1000, level: (short) (3 - second));
        Add(10200032, tick: 1500, level: second);

        Buff buff = Only(10200032);
        Assert.Multiple(() => {
            Assert.That(buff.Level, Is.EqualTo(level));
            Assert.That(buff.Stacks, Is.EqualTo(stacks));
        });
    }

    // A saved buff comes back on login with the time it had left (the World takes the time out of play off one not
    // on game time); one with no time left does not come back. 90000080 (KR: useInGameTime 1, 1 h), 10000051 (not set).
    [TestCase(10000051, 250000, 250000)]
    [TestCase(90000080, 250000, 250000)]
    [TestCase(10000051, 0, -1)]
    public void Login_RestoresTheSavedTime(int id, int remaining, long left) {
        Start(MapType.None, Effect(90000080, Plain with { DurationTick = 3600000, UseInGameTime = true }), Effect(10000051, Plain));

        Buffs.SetCacheBuffs([new BuffInfo { Id = id, Level = 1, MsRemaining = remaining, Stacks = 1, Enabled = true }], 5000);

        Buff? buff = Buffs.EnumerateBuffs(id).SingleOrDefault();
        Assert.That(buff == null ? -1 : buff.EndTick - buff.StartTick, Is.EqualTo(left));
    }

    // exp: a buff with exp gives its holder that exp when it is put on. 90000255 (KR: exp 264539, 100 ms).
    [Test]
    public void ExpBuff_GivesItsExp() {
        Start(MapType.None, Effect(90000255, Plain with { DurationTick = 100, Exp = 264539 }));
        long before = Player.Value.Character.Exp;

        Add(90000255);

        Assert.That(Player.Value.Character.Exp - before, Is.EqualTo(264539));
    }

    // rideId: the buff puts its player on the ride, and the buff and the ride end together. 73000002 (KR: rideId
    // 99910380, 30 s); the ride's stats are left out here.
    private void Rides(params int[] rides) {
        var storage = new RideMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(RideMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(storage)!;
        foreach (int ride in rides) {
            var metadata = new RideMetadata(ride, "", new RideMetadataBasic(RideOnType.Battle, 0, 0, 0, false, 0, 0),
                new RideMetadataSpeed(1, 1, 1, 1), new Dictionary<BasicAttribute, long>());
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [ride, metadata]);
        }
        typeof(FieldManager).GetField("<RideMetadata>k__BackingField", Any)!.SetValue(Field, storage);
        test.Session.Ride = new RideManager(test.Session);
        // Getting off sends the key table again (the default one here).
        typeof(ConfigManager).GetField("session", Any)!.SetValue(test.Session.Config, test.Session);
        typeof(ConfigManager).GetField("keyBinds", Any)!.SetValue(test.Session.Config, new Dictionary<int, KeyBind>());
    }

    private static readonly AdditionalEffectMetadataProperty BattleRide = Plain with { DurationTick = 30000, RideId = 99910380 };

    [Test]
    public void RideId_MountsTheRide() {
        Start(MapType.None, Effect(73000002, BattleRide));
        Rides(99910380);

        Add(73000002);

        Ride? ride = test.Session.Ride.Ride;
        Assert.That(ride?.Metadata.Id, Is.EqualTo(99910380));
        Assert.That((ride?.Action as RideOnActionBattle)?.SkillId, Is.EqualTo(73000002));
    }

    [Test]
    public void RideId_BuffEndGetsOff() {
        Start(MapType.None, Effect(73000002, BattleRide));
        Rides(99910380);
        Add(73000002);

        Buffs.Remove(73000002, Player.ObjectId);

        Assert.That(test.Session.Ride.Ride, Is.Null);
    }

    [Test]
    public void RideId_GettingOffEndsTheBuff() {
        Start(MapType.None, Effect(73000002, BattleRide));
        Rides(99910380);
        Add(73000002);

        test.Session.Ride.Dismount(RideOffType.Default);

        Assert.That(Held(), Is.Empty);
    }

    // A ride with no ride row is not put on, so the buff does not stay either.
    [Test]
    public void RideId_UnknownRideDropsTheBuff() {
        Start(MapType.None, Effect(73000002, BattleRide));
        Rides();

        Add(73000002);

        Assert.That(Held(), Is.Empty);
        Assert.That(test.Session.Ride.Ride, Is.Null);
    }

    // skillUseDisable ids: a held buff stops the skills it lists, until it ends. 10100181 (KR: 13 skills, among them
    // 10100051; 20 s).
    [Test]
    public void SkillUseDisableIds_StopsTheListedSkills() {
        Start(MapType.None, Effect(10100181, Plain with { DurationTick = 20000, SkillUseDisableIds = [10100051, 10100061, 10100071] }));
        Add(10100181);

        Assert.That(Buffs.SkillUseDisabled(10100051), Is.True);
        Assert.That(Buffs.SkillUseDisabled(10100052), Is.False);

        Buffs.Remove(10100181, Player.ObjectId);

        Assert.That(Buffs.SkillUseDisabled(10100051), Is.False);
    }

    // skillUseDisable main-type mask: the held buffs' masks add up. 71000051 (KR: 2), 49018002 (KR: 12).
    [Test]
    public void SkillUseDisableMask_AddsUpTheHeldMasks() {
        Start(MapType.None, Effect(71000051, Plain with { SkillUseDisableMask = 2 }), Effect(49018002, Plain with { SkillUseDisableMask = 12 }));
        Add(71000051);

        Assert.That(Buffs.SkillUseDisableMask(), Is.EqualTo(2));

        Add(49018002);

        Assert.That(Buffs.SkillUseDisableMask(), Is.EqualTo(14));
    }

    private static AdditionalEffectMetadata Updating(int id, AdditionalEffectMetadataProperty property, AdditionalEffectMetadataUpdate update) =>
        Effect(id, property) with { Update = update };

    private static AdditionalEffectMetadataUpdate.CancelEffect Cancel(int[] ids, BuffCategory[] categories, bool sameCaster = false) =>
        new(sameCaster, false, ids, categories);

    // cancelEffectCodes: the start takes off the effects it names. 10100181 (KR: cancels 10100061).
    [Test]
    public void CancelIds_TakeOffTheNamedBuff() {
        Start(MapType.None,
            Updating(10100181, Plain with { DurationTick = 20000 }, new AdditionalEffectMetadataUpdate(Cancel([10100061], []), [], [], [], [])),
            Effect(10100061, Plain), Effect(10000051, Plain));
        Add(10100061);
        Add(10000051);

        Add(10100181);

        Assert.That(Held(), Is.EqualTo(new[] { 10000051, 10100181 }));
    }

    // cancelCheckSameCaster: only the copies the same caster put on go. 10200253 (KR: cancels 10200243, same caster).
    [TestCase(false, new[] { 10200253 })]
    [TestCase(true, new[] { 10200243, 10200253 })]
    public void CancelSameCaster_LeavesOtherCastersBuffs(bool otherCaster, int[] held) {
        Start(MapType.None,
            Updating(10200253, Plain, new AdditionalEffectMetadataUpdate(Cancel([10200243], [], sameCaster: true), [], [], [], [])),
            Effect(10200243, Plain));
        Add(10200243, caster: otherCaster ? Field.FieldActor : null);

        Add(10200253);

        Assert.That(Held(), Is.EqualTo(held));
    }

    // cancelEffectCategories: the start takes off the buffs of the category. 90000190 (KR: category 7), 10000031 (KR:
    // category 7), 10200062 (KR: category 8).
    [Test]
    public void CancelCategories_TakeOffTheCategory() {
        Start(MapType.None,
            Updating(90000190, Plain, new AdditionalEffectMetadataUpdate(Cancel([], [BuffCategory.Stunned]), [], [], [], [])),
            Effect(10000031, Plain with { Category = BuffCategory.Stunned }), Effect(10200062, Plain with { Category = BuffCategory.Slow }));
        Add(10000031);
        Add(10200062);

        Add(90000190);

        Assert.That(Held(), Is.EqualTo(new[] { 10200062, 90000190 }));
    }

    // immuneEffectCodes: while held, the named effect is not put on. 10100051 (KR: immune to 10100052).
    [Test]
    public void ImmuneIds_KeepTheNamedBuffOff() {
        Start(MapType.None,
            Updating(10100051, Plain, new AdditionalEffectMetadataUpdate(null, [10100052], [], [], [])),
            Effect(10100052, Plain), Effect(10000051, Plain));
        Add(10100051);

        Add(10100052);
        Add(10000051);

        Assert.That(Held(), Is.EqualTo(new[] { 10000051, 10100051 }));
    }

    // immuneEffectCategories: while held, no buff of the category is put on. 40500047 (KR: category 8), 10200062.
    [Test]
    public void ImmuneCategories_KeepTheCategoryOff() {
        Start(MapType.None,
            Updating(40500047, Plain, new AdditionalEffectMetadataUpdate(null, [], [BuffCategory.Slow], [], [])),
            Effect(10200062, Plain with { Category = BuffCategory.Slow }), Effect(10000031, Plain with { Category = BuffCategory.Stunned }));
        Add(40500047);

        Add(10200062);
        Add(10000031);

        Assert.That(Held(), Is.EqualTo(new[] { 10000031, 40500047 }));
    }

    // ModifyOverlapCount: the start changes the stacks of another effect held, and one that comes to 0 goes.
    // 10200036 (KR: +10 on 10200032, maxBuffCount 10); 10300263 (KR: -1 on 10300262, maxBuffCount 5).
    [Test]
    public void ModifyOverlapCount_AddsStacks() {
        Start(MapType.None,
            Effect(10200036, Plain with { MaxCount = 1 }) with { ModifyOverlapCount = [new AdditionalEffectMetadataModifyOverlapCount(10200032, 10)] },
            Effect(10200032, Plain with { MaxCount = 10 }));
        Add(10200032);

        Add(10200036);

        Assert.That(Only(10200032).Stacks, Is.EqualTo(10));
    }

    [Test]
    public void ModifyOverlapCount_TakesOffAtNoStacks() {
        Start(MapType.None,
            Effect(10300263, Plain with { MaxCount = 1 }) with { ModifyOverlapCount = [new AdditionalEffectMetadataModifyOverlapCount(10300262, -1)] },
            Effect(10300262, Plain with { MaxCount = 5 }));
        Add(10300262);

        Add(10300263);

        Assert.That(Held(), Is.EqualTo(new[] { 10300263 }));
    }

    // eventBuffType 1 (auto fishing): the holder fishes on its own. 100000011 (KR: 3 h).
    [Test]
    public void EventTypeAutoFish_IsHeld() {
        Start(MapType.None, Effect(100000011, Plain with { DurationTick = 10800000, EventType = BuffEventType.AutoFish }), Effect(10000051, Plain));
        Add(10000051);

        Assert.That(Buffs.HasBuff(BuffEventType.AutoFish), Is.False);

        Add(100000011);

        Assert.That(Buffs.HasBuff(BuffEventType.AutoFish), Is.True);
    }

    // ResetCooldown: the start ends the cooldowns of the skills it names. 10200201 (KR: resets 10200051).
    [Test]
    public void ResetCooldown_EndsTheNamedCooldown() {
        Start(MapType.None, Updating(10200201, Plain, new AdditionalEffectMetadataUpdate(null, [], [], [10200051], [])));
        ConfigManager config = test.Session.Config;
        typeof(ConfigManager).GetField("session", Any)!.SetValue(config, test.Session);
        typeof(ConfigManager).GetField("skillCooldowns", Any)!.SetValue(config, new Dictionary<int, SkillCooldown>());
        typeof(ConfigManager).GetField("lastCasts", Any)!.SetValue(config, new Dictionary<int, Maple2.Server.Game.Util.SkillCastRules.LastCast>());
        config.SetSkillCooldown(10200051, endTick: 60000);
        config.SetSkillCooldown(10200052, endTick: 60000);

        Add(10200201);

        Assert.That(config.GetSkillCooldown(10200051)?.EndTick, Is.EqualTo(0));
        Assert.That(config.GetSkillCooldown(10200052)?.EndTick, Is.EqualTo(60000));
    }

    // shield hpValue: the buff takes that much damage before it breaks. 62100016 (KR: level 1 hpValue 95000).
    [Test]
    public void ShieldHpValue_GivesTheShield() {
        Start(MapType.None, Effect(62100016, Plain) with { Shield = new AdditionalEffectMetadataShield(95000, 0f) });
        Add(62100016);

        Assert.That(Only(62100016).ShieldHealth, Is.EqualTo(95000));
    }

    // shield hpByTargetMaxHP: the shield is that share of the holder's maximum health, rounded down. 10300131 (KR:
    // level 1 hpByTargetMaxHP 0.06).
    [Test]
    public void ShieldByTargetMaxHp_TakesAShareOfTheHoldersHealth() {
        Start(MapType.None, Effect(10300131, Plain) with { Shield = new AdditionalEffectMetadataShield(0, 0.06f) });
        Player.Stats.Values[BasicAttribute.Health].AddTotal(50010 - Player.Stats.Values[BasicAttribute.Health].Total);
        Add(10300131);

        Assert.That(Only(10300131).ShieldHealth, Is.EqualTo(3000));
    }

    // dotBuff: each proc gives the second buff to the caster (target 0, the default) or to the holder (target 1).
    // 10200035 (KR: dotBuff 10200036 level 1, no target, interval 1000, 1 s); 70001041 (KR: dotBuff 70001011 level 1,
    // target 1, interval 1000, 3 s). The field actor casts both on the player.
    [TestCase(10200035, 10200036, DotTargetType.Caster, false, true)]
    [TestCase(70001041, 70001011, DotTargetType.Owner, true, false)]
    public void DotBuff_GoesToTheCasterOrTheHolder(int id, int given, DotTargetType target, bool holder, bool caster) {
        Start(MapType.None,
            Effect(id, Plain with { DurationTick = 3000, IntervalTick = 1000 }) with {
                Dot = new AdditionalEffectMetadataDot(null, new AdditionalEffectMetadataDot.DotBuff(target, given, 1)),
            },
            Effect(given, Plain));
        Add(id, tick: 1000, caster: Field.FieldActor);
        Buffs.Update(1000);

        Assert.Multiple(() => {
            Assert.That(Buffs.EnumerateBuffs(given).Any(), Is.EqualTo(holder));
            // The caster's buffs as the effect reaches them (IActor.Buffs; FieldActor hides it with a second manager).
            Assert.That(((IActor) Field.FieldActor).Buffs.EnumerateBuffs(given).Any(), Is.EqualTo(caster));
        });
    }

    // tickSkill: each proc casts its skills on the holder (owner 1, target 1). 10200183 (KR: tickSkill 10200191 level
    // 1, owner and target 1, probability 1, interval 2000, 2 s).
    [Test]
    public void TickSkill_GivesTheHolderItsSkillOnAProc() {
        AdditionalEffectMetadata held = Effect(10200183, Plain with { DurationTick = 2000, IntervalTick = 2000 });
        var tick = new SkillEffectMetadata(1, [new SkillEffectMetadata.Skill(10200191, 1)],
            new SkillEffectMetadataCondition(held.Condition, SkillTargetType.Owner, SkillTargetType.Owner, 1, false), null);
        Start(MapType.None, held with { TickSkills = [tick], Dot = new AdditionalEffectMetadataDot(null, null) }, Effect(10200191, Plain));
        Add(10200183, tick: 1000);
        Assert.That(Held(), Is.EqualTo(new[] { 10200183 }));

        Buffs.Update(1000);

        Assert.That(Held(), Is.EqualTo(new[] { 10200183, 10200191 }));
    }

    // reflectionAdditionalEffectId: a hit on the holder gives its attacker the reflect effect. 50003300 (KR:
    // reflectionRate 1, reflectionCount 99999, reflectionAdditionalEffectId 50003301 level 1).
    [Test]
    public void Reflect_GivesTheAttackerTheEffect() {
        Start(MapType.None,
            Effect(50003300, Plain) with {
                Reflect = new AdditionalEffectMetadataReflect(1f, 50003301, 1, 99999, 0, 0, new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>()),
            },
            Effect(50003301, Plain));
        Add(50003300);

        Player.Reflect(Field.FieldActor);

        Assert.That(((IActor) Field.FieldActor).Buffs.EnumerateBuffs(50003301), Has.Count.EqualTo(1));
    }

    // Status rates: a held buff's rate adds that share of the stat. 10100071 (KR: Rates Defense -0.01, one stack) on
    // a defense of 1000 gives 990.
    [Test]
    public void StatusRate_AddsItsShareOfTheStat() {
        AdditionalEffectMetadata metadata = DisabledBuffTests.Effect(10100071, rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, -0.01f } });
        var stats = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Defense, 1000 } }, 0);

        StatsManager.AddBuffs(stats, [DisabledBuffTests.Held(metadata, enabled: true)]);
        stats.Total();

        Assert.That(stats[BasicAttribute.Defense].Total, Is.EqualTo(990));
    }
}
