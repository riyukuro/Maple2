using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
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
        MapMetadata map = TestGameSession.Map(2000001);
        Start(map with { Property = map.Property with { Type = type } }, effects);
    }

    private void Start(MapMetadata map, params AdditionalEffectMetadata[] effects) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(map);
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
    internal static AdditionalEffectMetadata Effect(int id, AdditionalEffectMetadataProperty property) {
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

    // cancelEffectCodes: the cancel takes the named buff whoever gave it, a casterIndividual copy of another caster too;
    // cancelBuffCategories alone (no codes) takes the buffs of the category. 10200112 (KR: casterIndividualEffect 1);
    // 90000070 cancels category 2001 only.
    [Test]
    public void Cancel_TakesAnotherCastersCopyAndCategoriesAlone() {
        // Effects that proc each second with no dot; a canceller's start cancels, and so does each proc.
        AdditionalEffectMetadata Ticking(int id, AdditionalEffectMetadataProperty property) =>
            Effect(id, property with { IntervalTick = 1000 }) with { Dot = new AdditionalEffectMetadataDot(null, null) };
        AdditionalEffectMetadata Cancel(int id, int[] ids, BuffCategory[] categories) => Ticking(id, Plain) with {
            Update = new AdditionalEffectMetadataUpdate(new AdditionalEffectMetadataUpdate.CancelEffect(false, false, ids, categories), [], [], [], []),
        };
        Start(MapType.None,
            Ticking(10200112, Plain with { CasterIndividualBuff = true }),
            Ticking(50000001, Plain with { Category = (BuffCategory) 2001 }),
            Ticking(10000051, Plain),
            Cancel(10200243, [10200112], []),
            Cancel(90000070, [], [(BuffCategory) 2001]));
        Add(10200243);
        Add(90000070);
        // Put on after the cancellers started, so only their activation and procs can take them.
        Add(10200112, caster: Field.FieldActor);
        Add(50000001, caster: Field.FieldActor);
        Add(10000051);
        Assert.That(Held(), Is.EqualTo(new[] { 10000051, 10200112, 10200243, 50000001, 90000070 }));
        Buffs.Update(2100);

        Assert.That(Held(), Is.EqualTo(new[] { 10000051, 10200243, 90000070 }));
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

    // A buff without an end that has an interval procs on it for as long as it is held, with no count limit:
    // 60126002 (KR: keepCondition 99, intervalTick 3500, an interval skill) procs at the add and every 3.5 s after.
    // Without an interval it never procs.
    [TestCase(3500, 29)]
    [TestCase(0, 0)]
    public void UnlimitedDuration_ProcsOnItsInterval(int interval, int procs) {
        Start(MapType.None, Effect(60126002, Plain with { DurationTick = 0, IntervalTick = interval, KeepCondition = BuffKeepCondition.UnlimitedDuration }) with { Dot = new AdditionalEffectMetadataDot(null, null) });
        Add(60126002, tick: 1000);

        for (long tick = 1000; tick <= 100000; tick += 500) {
            Buffs.Update(tick);
        }

        Assert.That(Held(), Is.EqualTo(new[] { 60126002 }));
        Assert.That(Only(60126002).ProcCount, Is.EqualTo(procs));
    }

    // A dotCondition-1 one waits one interval first. 70000201 (KR: keepCondition 99, intervalTick 1000, dotCondition 1).
    [Test]
    public void UnlimitedDuration_DotCondition1WaitsAnInterval() {
        Start(MapType.None, Effect(70000201, Plain with {
            DurationTick = 0, IntervalTick = 1000, KeepCondition = BuffKeepCondition.UnlimitedDuration, DotCondition = BuffDotCondition.OnInterval,
        }) with { Dot = new AdditionalEffectMetadataDot(null, null) });
        Add(70000201, tick: 1000);
        Buff buff = Only(70000201);

        Buffs.Update(1999);
        Assert.That(buff.ProcCount, Is.EqualTo(0));
        Buffs.Update(2000);
        Buffs.Update(3000);
        Assert.That(buff.ProcCount, Is.EqualTo(2));
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

    // clearCondition: the holder taking a listed state ends the buff, any other state leaves it. Dark Sight 10800051
    // (KR: 3,4,6,7,8,11,12) beside a plain buff; JumpTo (7) is listed, Walk (2) and Hit (13) are not.
    [Test]
    public void ClearCondition_EndsTheBuffOnAListedState() {
        ActorState[] darkSight = AdditionalEffectMapper.ClearConditions([3, 4, 6, 7, 8, 11, 12])!;
        Start(MapType.None, Effect(10800051, Plain with { ClearConditions = darkSight }), Effect(10000051, Plain));
        Add(10800051);
        Add(10000051);

        Player.State = ActorState.Walk;
        Player.State = ActorState.Hit;
        Assert.That(Held(), Is.EqualTo(new[] { 10000051, 10800051 }));

        Player.State = ActorState.JumpTo;
        Assert.That(Held(), Is.EqualTo(new[] { 10000051 }));
        Assert.Multiple(() => {
            Assert.That(AdditionalEffectMapper.ClearConditions([0]), Is.Null, "a 0 alone is no state");
            Assert.That(AdditionalEffectMapper.ClearConditions(null), Is.Null);
            Assert.That(AdditionalEffectMapper.ClearConditions([6, 7, 13]), Is.EqualTo(new[] { ActorState.Jump, ActorState.JumpTo, ActorState.Hit }));
        });
    }

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

    // An immune holder whose begin condition fails is not enabled, and a buff that is not enabled keeps nothing off
    // (the has-buff convention: only enabled buffs count). KR 10500292 is immune to 10500298 only while its owner holds
    // 10500293; without it 10500298 lands.
    [TestCase(false, new[] { 10500292, 10500298 })]
    [TestCase(true, new[] { 10500292, 10500293 })]
    public void ImmuneIds_OnlyAnEnabledHolderKeepsTheBuffOff(bool holdsSwitch, int[] held) {
        var needs293 = new BeginConditionTarget([new BeginConditionTarget.HasBuff(10500293, 0, false, 0, CompareType.Equals)],
            new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [], [], [],
            new Dictionary<MasteryType, int>(), [], []);
        AdditionalEffectMetadata holder = Updating(10500292, Plain, new AdditionalEffectMetadataUpdate(null, [10500298], [], [], []));
        Start(MapType.None, holder with { Condition = holder.Condition with { Owner = needs293 } },
            Effect(10500293, Plain), Effect(10500298, Plain));
        if (holdsSwitch) {
            Add(10500293);
        }
        Add(10500292);
        Assert.That(Only(10500292).Enabled, Is.EqualTo(holdsSwitch));

        Add(10500298);

        Assert.That(Held(), Is.EqualTo(held));
    }

    // The holder's condition is tested when a buff comes, not taken from its last update: 10500293 gained after the
    // holder (no update between) makes it immune to 10500298 at once, and 10500293 lost ends that at once.
    [TestCase(false, new[] { 10500292, 10500293 })]
    [TestCase(true, new[] { 10500292, 10500298 })]
    public void ImmuneIds_TheHoldersConditionIsTestedWhenTheBuffComes(bool switchLost, int[] held) {
        var needs293 = new BeginConditionTarget([new BeginConditionTarget.HasBuff(10500293, 0, false, 0, CompareType.Equals)],
            new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [], [], [],
            new Dictionary<MasteryType, int>(), [], []);
        AdditionalEffectMetadata holder = Updating(10500292, Plain, new AdditionalEffectMetadataUpdate(null, [10500298], [], [], []));
        Start(MapType.None, holder with { Condition = holder.Condition with { Owner = needs293 } },
            Effect(10500293, Plain), Effect(10500298, Plain));
        if (switchLost) {
            Add(10500293);
            Add(10500292);
            Assert.That(Only(10500292).Enabled, Is.True);
            Buffs.Remove(10500293, Player.ObjectId);
        } else {
            Add(10500292);
            Assert.That(Only(10500292).Enabled, Is.False);
            Add(10500293);
        }

        Add(10500298);

        Assert.That(Held(), Is.EqualTo(held));
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

    // A hit on a shield's holder: the shield takes it whole while it holds (a Block of 0, and the holder's buff update
    // carries what is left), and a hit beyond it breaks the shield, which takes off the buff and lets the rest
    // through. 62100016 (KR: level 1 hpValue 95000) on a player whose even weapon attack makes every hit the same.
    [Test]
    public void Shield_TakesTheHitUntilItBreaks() {
        Start(MapType.None, Effect(62100016, Plain) with { Shield = new AdditionalEffectMetadataShield(95000, 0f) });
        Stats stats = Player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        var hit = new DamagePropertyRecord { CanCrit = false, AttackType = AttackType.Physical, Rate = 1, SuperArmorBreak = true };
        (DamageType plainType, long plain) = DamageCalculator.CalculateDamage(Player, Player, hit);
        Assert.That((plainType, plain > 1000), Is.EqualTo((DamageType.Normal, true)));
        Assert.That(plain, Is.LessThan(95000 / 2));

        Add(62100016);
        test.TakeSent();
        Assert.That(DamageCalculator.CalculateDamage(Player, Player, hit), Is.EqualTo((DamageType.Block, 0L)));
        Assert.That(Only(62100016).ShieldHealth, Is.EqualTo(95000 - plain).Within(1));
        Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.Buff), Is.EqualTo(1));

        // Left with less than a hit: the shield breaks and takes its share.
        Only(62100016).ShieldHealth = 100;
        (DamageType type, long through) = DamageCalculator.CalculateDamage(Player, Player, hit);
        Assert.That(type, Is.EqualTo(DamageType.Normal));
        Assert.That(through, Is.EqualTo(plain - 100).Within(1));
        Assert.That(Held(), Is.Empty);
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

    // The reflect a buff gives goes with the buff: once it is taken off, a hit on the holder reflects nothing.
    // 50003300 (KR: reflectionRate 1, reflectionCount 99999, reflectionAdditionalEffectId 50003301 level 1).
    [Test]
    public void Reflect_EndsWithItsBuff() {
        Start(MapType.None,
            Effect(50003300, Plain) with {
                Reflect = new AdditionalEffectMetadataReflect(1f, 50003301, 1, 99999, 0, 0, new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>()),
            },
            Effect(50003301, Plain));
        Add(50003300);
        Assert.That(Buffs.Reflect?.SourceBuffId, Is.EqualTo(50003300));

        Buffs.Remove(50003300, Player.ObjectId);

        Assert.That(Buffs.Reflect, Is.Null);
        Player.Reflect(Field.FieldActor);
        Assert.That(((IActor) Field.FieldActor).Buffs.EnumerateBuffs(50003301), Is.Empty);
    }

    // ConsumeProperty spRate: the spirit rates of the buffs held add up, each times its stacks, and cut a cast's spirit
    // cost (SkillCastRules.SpiritCost); -1 makes casts free. 10300173 (KR: spRate -0.5, held here at 2 stacks of a
    // maximum of 2 where KR has 1) and 10500181 (KR: spRate -1).
    [Test]
    public void ConsumeSpRate_AddsUpByStacks() {
        Start(MapType.None,
            Effect(10300173, Plain with { MaxCount = 2 }) with { Consume = new AdditionalEffectMetadataConsume(0, -0.5f) },
            Effect(10500181, Plain with { DurationTick = 20000 }) with { Consume = new AdditionalEffectMetadataConsume(0, -1f) });
        Assert.That(Buffs.ConsumeSpRate, Is.Zero);

        Add(10300173);
        Assert.That(Buffs.ConsumeSpRate, Is.EqualTo(-0.5f));
        Assert.That(SkillCastRules.SpiritCost(20, 0, 0, Buffs.ConsumeSpRate), Is.EqualTo(10));
        Add(10300173);
        Assert.That(Only(10300173).Stacks, Is.EqualTo(2));
        Assert.That(Buffs.ConsumeSpRate, Is.EqualTo(-1f));
        Add(10500181);
        Assert.That(Buffs.ConsumeSpRate, Is.EqualTo(-2f));
        Assert.That(SkillCastRules.SpiritCost(20, 0, 0, Buffs.ConsumeSpRate), Is.EqualTo(0));
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
        Assert.That(((IActor) Field.FieldActor).Buffs.EnumerateBuffs(50003301).Single().Reflected, Is.False, "no reflect rate");
    }

    // 49126002 (KR: reflectionRate 1, physical and magical reflectionRate 0.02, reflectionDamageReduceRate 0.2, effect
    // 49126003: a dot of type 1, rate 0.05, every 100 ms for 150 ms): a hit of 1000 sends 16 back, which the effect's
    // next tick deals in place of its own roll; the tick after it, with nothing reflected, is not dealt. The player
    // hits itself, so it is both the holder and the attacker.
    [Test]
    public void Reflect_TheEffectTickDealsTheShare() {
        var dot = new AdditionalEffectMetadataDot.DotDamage(AttackType.Physical, Element.None, UseGrade: false, Rate: 0.05f, HpValue: 0,
            SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: false, NotKill: false);
        Start(MapType.None,
            Effect(49126002, Plain) with {
                Reflect = new AdditionalEffectMetadataReflect(1f, 49126003, 1, 99999, 0, 0, new Dictionary<BasicAttribute, long>(),
                    new Dictionary<BasicAttribute, float> { { BasicAttribute.PhysicalAtk, 0.02f }, { BasicAttribute.MagicalAtk, 0.02f } }, DamageReduceRate: 0.2f),
            },
            Effect(49126003, Plain with { DurationTick = 150, IntervalTick = 100 }) with { Dot = new AdditionalEffectMetadataDot(dot, null) });
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(Player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(Player, new RegenClock());
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(Player, new ConcurrentDictionary<int, DamageRecordTarget>());
        Stat health = Player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(1000 - health.Total);
        health.Add(1000 - health.Current);
        Add(49126002);

        Player.Reflect(Player, 1000, AttackType.Physical);
        Buff reflected = Only(49126003);
        Assert.That(reflected.Reflected, Is.True);
        Assert.That(reflected.ReflectedDamage, Is.EqualTo(16));

        reflected.Update(reflected.NextProcTick);
        Assert.That(health.Current, Is.EqualTo(984));
        Assert.That(reflected.ReflectedDamage, Is.EqualTo(0));

        reflected.Update(reflected.NextProcTick);
        Assert.That(health.Current, Is.EqualTo(984), "nothing reflected since");
    }

    // The reflect effect 49126003 is not caster-individual, so a second holder's hit pools on the same buff. Each
    // holder's share is its own tick, credited to that holder (the DotDamage caster), not all to the buff's first
    // caster.
    [Test]
    public void Reflect_EachHolderTicksItsOwnShare() {
        var dot = new AdditionalEffectMetadataDot.DotDamage(AttackType.Physical, Element.None, UseGrade: false, Rate: 0.05f, HpValue: 0,
            SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: false, NotKill: false);
        Start(MapType.None,
            Effect(49126002, Plain) with {
                Reflect = new AdditionalEffectMetadataReflect(1f, 49126003, 1, 99999, 0, 0, new Dictionary<BasicAttribute, long>(),
                    new Dictionary<BasicAttribute, float> { { BasicAttribute.PhysicalAtk, 0.02f }, { BasicAttribute.MagicalAtk, 0.02f } }, DamageReduceRate: 0.2f),
            },
            Effect(49126003, Plain with { DurationTick = 150, IntervalTick = 100 }) with { Dot = new AdditionalEffectMetadataDot(dot, null) });
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(Player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(Player, new RegenClock());
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(Player, new ConcurrentDictionary<int, DamageRecordTarget>());
        Stat health = Player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(1000 - health.Total);
        health.Add(1000 - health.Current);
        Add(49126002);

        Player.Reflect(Player, 1000, AttackType.Physical);
        Buff reflected = Only(49126003);
        reflected.AddReflected(Field.FieldActor, 30);
        Assert.That(reflected.ReflectedDamage, Is.EqualTo(46));
        test.TakeSent();

        reflected.Update(reflected.NextProcTick);
        Assert.That(health.Current, Is.EqualTo(954));
        // SkillDamage DotDamage (3): op, command, caster, target, buff, type, hp.
        int[] casters = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.SkillDamage && packet[2] == 3)
            .Select(packet => BitConverter.ToInt32(packet, 3)).ToArray();
        Assert.That(casters, Is.EquivalentTo(new[] { Player.ObjectId, Field.FieldActor.ObjectId }));
        // The holder's own share (16) is no one's damage; the field's share (30) is its caster's.
        Assert.That(Player.DamageByDealer, Is.EquivalentTo(new[] { (Field.FieldActor.ObjectId, 30L) }));
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

    // A cast's effect tests (SkillCastRules.EffectRefusal) count a buff state that changed within BuffStateSlackMs
    // both ways: a requireEffect buff just removed still counts as held, and a buff of a refused group just gained
    // does not refuse yet, as the client's buffs follow by packet. KR 10200281 (requireEffect of itself), group 10 of
    // the item skills 90000596-90000601.
    [Test]
    public void CastEffectTests_GiveAJustChangedBuffTheSlack() {
        Start(MapType.None, Effect(10200281, Plain), Effect(10000051, Plain with { Group = 10 }));
        long now = Field.FieldTick;
        long later = now + SkillCastRules.BuffStateSlackMs + 1;
        Assert.That(Buffs.HeldOrJustLost(10200281, now), Is.False, "never held");

        Add(10200281, tick: now);
        Add(10000051, tick: now);
        Assert.Multiple(() => {
            Assert.That(Buffs.HeldOrJustLost(10200281, later), Is.True, "held");
            Assert.That(Buffs.GroupHeldSettled(10, now), Is.False, "group 10 just gained");
            Assert.That(Buffs.GroupHeldSettled(10, later), Is.True, "group 10 held past the slack");
            Assert.That(Buffs.GroupHeldSettled(11, later), Is.False);
        });

        Buffs.Remove(10200281, Player.ObjectId);
        Buffs.Remove(10000051, Player.ObjectId);
        Assert.Multiple(() => {
            Assert.That(Buffs.HeldOrJustLost(10200281, now), Is.True, "just removed");
            Assert.That(Buffs.HeldOrJustLost(10200281, later), Is.False, "removed past the slack");
            Assert.That(Buffs.GroupHeldSettled(10, later), Is.False, "none held");
        });
    }

    // Only a move of Enabled is a gain or a loss for that slack: a battle mount suspend alone (Buff.EnabledChanges
    // rebuilds the sums without a packet) stamps neither tick.
    [Test]
    public void CastEffectTests_ASuspendOnlyMoveKeepsTheSlackTicks() {
        Start(MapType.None, Effect(10000051, Plain with { Group = 10 }));
        long now = Field.FieldTick;
        long later = now + SkillCastRules.BuffStateSlackMs + 1;
        Add(10000051, tick: now);
        Buff held = Buffs.EnumerateBuffs(10000051).Single();
        Field.GetType().GetProperty(nameof(Field.FieldTick))!.SetValue(Field, later);
        Buffs.EnabledChanged(held, enabledMoved: false);
        Assert.That(Buffs.GroupHeldSettled(10, later), Is.True, "the suspend did not restart the group's slack");
        Buffs.EnabledChanged(held, enabledMoved: true);
        Assert.That(Buffs.GroupHeldSettled(10, later), Is.False, "a move of Enabled does");
    }

    // A copy of the held buffs taken while the field thread removes one can hold a null slot without throwing; the
    // cast checks' copy drops it, and a copy that throws is taken again.
    [Test]
    public void CopyFromHandler_DropsARacedSlotAndRetries() {
        Buff held = DisabledBuffTests.Held(DisabledBuffTests.Effect(10000051), enabled: true);
        Assert.That(BuffManager.CopyFromHandler(() => [held, null!]), Is.EqualTo(new[] { held }));
        int calls = 0;
        List<Buff> copied = BuffManager.CopyFromHandler(() => ++calls < 3 ? throw new InvalidOperationException() : [held]);
        Assert.Multiple(() => {
            Assert.That(copied, Is.EqualTo(new[] { held }));
            Assert.That(calls, Is.EqualTo(3));
        });
    }

    // clearDistanceFromCaster (KR 62100022: 300, a 20 s buff): the buff ends at its next update once its holder is
    // farther than that from its caster, and stays while within it (exactly 300 still holds).
    [TestCase(300f, true)]
    [TestCase(301f, false)]
    public void ClearDistanceFromCaster_EndsTheBuffPastTheDistance(float distance, bool kept) {
        Start(MapType.None, Effect(62100022, Plain with { DurationTick = 20000, ClearOnDistanceFromCaster = 300 })
            with { Dot = new AdditionalEffectMetadataDot(null, null) });
        Field.FieldActor.Position = System.Numerics.Vector3.Zero;
        Player.Position = new System.Numerics.Vector3(distance, 0, 0);
        Add(62100022, caster: Field.FieldActor);

        Buffs.Update(1100);

        Assert.That(Held(), kept ? Is.EqualTo(new[] { 62100022 }) : Is.Empty);
    }

    // A recovery buff's proc (Buff.ApplyRecovery): 10000051 (KR: hpRate / spRate 0.11, epValue 40, no crit, 100 ms)
    // heals the holder's health, spirit and stamina by its heal record, sends the holder's stat update and a
    // SkillDamage Heal, and with the DPS meter open the health restored after the cap: as given and as received for a
    // self-cast heal, nothing for a holder at full health.
    [TestCase(true)]
    [TestCase(false)]
    public void Recovery_HealsTheHolderAndSendsTheRestoredPart(bool hurt) {
        var recovery = new AdditionalEffectMetadataRecovery(RecoveryRate: 0, HpValue: 0, HpRate: 0.11f, HpConsumeRate: 0,
            SpValue: 0, SpRate: 0.11f, SpConsumeRate: 0, EpValue: 40, EpRate: 0, DisableCrit: true);
        Start(MapType.None, Effect(10000051, Plain with { DurationTick = 100, IntervalTick = 100 })
            with { Recovery = recovery, Dot = new AdditionalEffectMetadataDot(null, null) });
        test.Session.DpsMeterOpen = true;
        Stat health = Player.Stats.Values[BasicAttribute.Health];
        Stat spirit = Player.Stats.Values[BasicAttribute.Spirit];
        Stat stamina = Player.Stats.Values[BasicAttribute.Stamina];
        (health.Total, health.Current) = (10000, hurt ? 1 : 10000);
        (spirit.Total, spirit.Current) = (100, 0);
        (stamina.Total, stamina.Current) = (120, 0);
        long healthBefore = health.Current;
        var record = new HealDamageRecord(Player, Player, 0, recovery);
        Assert.That(record.HpAmount, Is.GreaterThan(0));
        Add(10000051);
        test.TakeSent();

        Buffs.Update(1000);

        long restored = health.Current - healthBefore;
        Assert.That(restored, Is.EqualTo(hurt ? Math.Min(record.HpAmount, health.Total - 1) : 0));
        Assert.That(spirit.Current, Is.EqualTo(Math.Min(record.SpAmount, spirit.Total)).And.GreaterThan(0));
        Assert.That(stamina.Current, Is.EqualTo(Math.Min(40, stamina.Total)));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Stat), Is.GreaterThanOrEqualTo(1));
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.SkillDamage && packet[2] == 4), Is.EqualTo(1));
        (bool Received, int Amount)[] effective = sent.Where(packet => TestGameSession.Op(packet) == SendOp.DpsStat && packet[2] == 2)
            .Select(packet => (packet[3] != 0, BitConverter.ToInt32(packet, 4))).ToArray();
        Assert.That(effective, Is.EqualTo(hurt ? new[] { (false, (int) restored), (true, (int) restored) } : Array.Empty<(bool, int)>()));
    }

    // One add is one Buff add packet to the holder (and to each other player in the field), whatever the effect.
    [Test]
    public void Add_SendsOneAddPacketToTheHolder() {
        Start(MapType.None, Effect(10000051, Plain));

        Add(10000051);

        int adds = test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.Buff);
        Assert.That(adds, Is.EqualTo(1));
    }

    // ModifyEffectDuration on a proc: 50003004 (KR: factor 2 on 50003000) doubles the time left of the held
    // 50003000 and sends its update; a copy without an end (73000004 made unlimited here) is left alone.
    [Test]
    public void ModifyDuration_ScalesTheTimeLeftOfTheNamedBuff() {
        var update = new AdditionalEffectMetadataUpdate(null, [], [], [], [
            new AdditionalEffectMetadataUpdate.ModifyDuration(50003000, 2f, 0f),
            new AdditionalEffectMetadataUpdate.ModifyDuration(73000004, 2f, 0f),
        ]);
        Start(MapType.None,
            Effect(50003000, Plain with { DurationTick = 26000 }) with { Dot = new AdditionalEffectMetadataDot(null, null) },
            Effect(73000004, Plain with { DurationTick = 0, KeepCondition = BuffKeepCondition.UnlimitedDuration }) with { Dot = new AdditionalEffectMetadataDot(null, null) },
            Effect(50003004, Plain with { DurationTick = 1000 }) with { Update = update, Dot = new AdditionalEffectMetadataDot(null, null) });
        long now = Field.FieldTick;
        Add(50003000, tick: now);
        Add(73000004, tick: now);
        long unlimitedEnd = Only(73000004).EndTick;
        Add(50003004, tick: now);
        test.TakeSent();

        Buffs.Update(now + 100);

        Assert.That(Only(50003000).EndTick, Is.EqualTo(Buff.ModifiedEndTick(now + 26000, Field.FieldTick, 2f, 0f)));
        Assert.That(Only(50003000).EndTick, Is.GreaterThan(now + 26000));
        Assert.That(Only(73000004).EndTick, Is.EqualTo(unlimitedEnd));
        Assert.That(test.TakeSentOps().Count(op => op == SendOp.Buff), Is.GreaterThanOrEqualTo(1));
    }

    // An interact object's modify code (BuffManager.ModifyDuration): each held copy of the buff ends that many ms
    // later, and the field hears it.
    [Test]
    public void ModifyDurationByInteract_MovesTheEnd() {
        Start(MapType.None, Effect(50003000, Plain with { DurationTick = 26000 }));
        Add(50003000, tick: 1000);
        test.TakeSent();

        Buffs.ModifyDuration(50003000, 5000);
        Buffs.ModifyDuration(50003001, 5000);

        Assert.That(Only(50003000).EndTick, Is.EqualTo(32000));
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Buff }));
    }

    // A buff's timer ends it once its procs are spent: 10000051 (KR: 100 ms, one proc) is held through its proc and
    // gone at the first update past its end.
    [Test]
    public void TheTimerEndsABuffWhoseProcsAreSpent() {
        Start(MapType.None, Effect(10000051, Plain with { DurationTick = 1000, IntervalTick = 0 }) with { Dot = new AdditionalEffectMetadataDot(null, null) });
        Add(10000051, tick: 1000);

        Buffs.Update(1100);
        Assert.That(Only(10000051).ProcCount, Is.EqualTo(1));
        Buffs.Update(2000);
        Assert.That(Held(), Is.EqualTo(new[] { 10000051 }), "at its end");
        Buffs.Update(2001);
        Assert.That(Held(), Is.Empty);
    }

    // A buff whose begin condition stops holding is disabled and does not proc; once it holds again its next proc is
    // the first of its schedule at or after then, the missed ones not paid (ResumedProcTick). 70001111 (KR:
    // requireStates gosEmotion) with a proc each second.
    [Test]
    public void ADisabledBuffSkipsItsProcsAndResumesOnItsSchedule() {
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [],
            [ActorState.Emotion], [], new Dictionary<MasteryType, int>(), [], []);
        AdditionalEffectMetadata effect = Effect(70001111, Plain with { DurationTick = 10000, IntervalTick = 1000 })
            with { Dot = new AdditionalEffectMetadataDot(null, null) };
        Start(MapType.None, effect with { Condition = effect.Condition with { Owner = owner } });
        Player.State = ActorState.Emotion;
        Add(70001111, tick: 1000);
        Buff buff = Only(70001111);
        Buffs.Update(1000);
        Assert.That(buff.ProcCount, Is.EqualTo(1));

        Player.State = ActorState.Idle;
        Buffs.Update(2000);
        Buffs.Update(3000);
        Assert.That(buff.Enabled, Is.False);
        Assert.That(buff.ProcCount, Is.EqualTo(1), "no proc while disabled");

        Player.State = ActorState.Emotion;
        Buffs.Update(3500);
        Assert.That(buff.Enabled, Is.True);
        Assert.That(buff.NextProcTick, Is.EqualTo(4000));
        Assert.That(buff.ProcCount, Is.EqualTo(1));
        Buffs.Update(4000);
        Assert.That(buff.ProcCount, Is.EqualTo(2));
    }

    // A worn item's socketed gem gives its effects with the item's own (AddItemBuffs) and they go with the item
    // (RemoveItemBuffs); its effect ids are the item's (ItemEffectIds). KR gem 40220121 gives 90051021 and 90051022.
    [Test]
    public void AGemsEffectsComeAndGoWithItsItem() {
        Start(MapType.None, Effect(90051021, Plain with { DurationTick = 0, KeepCondition = BuffKeepCondition.UnlimitedDuration }),
            Effect(90051022, Plain with { DurationTick = 0, KeepCondition = BuffKeepCondition.UnlimitedDuration }),
            Effect(90000001, Plain with { DurationTick = 0, KeepCondition = BuffKeepCondition.UnlimitedDuration }));
        test.Register(TestGameSession.Metadata(40220121) with {
            AdditionalEffects = [new ItemMetadataAdditionalEffect(90051021, 1, false), new ItemMetadataAdditionalEffect(90051022, 1, false)],
        });
        var item = new Item(TestGameSession.Metadata(11400001) with { AdditionalEffects = [new ItemMetadataAdditionalEffect(90000001, 1, false)] }) {
            Socket = new ItemSocket(2, [new ItemGemstone(40220121, stats: new ItemStats()), null]),
        };

        Buffs.AddItemBuffs(item);
        Assert.That(Held(), Is.EqualTo(new[] { 90000001, 90051021, 90051022 }));
        Assert.That(Buffs.ItemEffectIds(item).Order(), Is.EqualTo(new[] { 90000001, 90051021, 90051022 }));

        Buffs.RemoveItemBuffs(item);
        Assert.That(Held(), Is.Empty);
    }

    // Entering a field gives its entrance buffs (the map's EntranceBuffs), and a Shadow World map (region 202) the
    // player the constants' shadowWorldBuffHpUp and shadowWorldBuffMoveProtect (KR 70000027 and 70000032).
    [TestCase(MapRegion.ShadowWorld, new[] { 61000010, 70000027, 70000032 })]
    [TestCase(MapRegion.None, new[] { 61000010 })]
    public void EnterField_GivesTheEntranceAndShadowWorldBuffs(MapRegion region, int[] held) {
        MapMetadata map = TestGameSession.Map(2000001);
        Start(map with { Property = map.Property with { Region = region }, EntranceBuffs = [new MapEntranceBuff(61000010, 1)] },
            Effect(61000010, Plain), Effect(70000027, Plain), Effect(70000032, Plain));
        test.SetServerTable(test.Session.ServerTableMetadata.ConstantsTable with {
            shadowWorldBuffHpUp = 70000027, shadowWorldBuffMoveProtect = 70000032,
        });

        EnterField();

        Assert.That(Held(), Is.EqualTo(held));
    }

    // The adventure level abilities (KR adventurelevelability 1: buff 100000028 from level 5, a step every 50 levels;
    // 2: 100000029 from level 10, so its second step at 60): the player holds each ability's buff at its step for the prestige level, and none
    // below the first step. A held step is not given again.
    [TestCase(4, new int[0], new short[0])]
    [TestCase(5, new[] { 100000028 }, new short[] { 1 })]
    [TestCase(59, new[] { 100000028, 100000029 }, new short[] { 2, 1 })]
    [TestCase(60, new[] { 100000028, 100000029 }, new short[] { 2, 2 })]
    public void PrestigeAbilities_GiveTheirBuffAtTheStep(int prestigeLevel, int[] held, short[] levels) {
        Start(MapType.None, Effect(100000028, Plain), Effect(100000028, Plain) with { Level = 2 }, Effect(100000029, Plain),
            Effect(100000029, Plain) with { Level = 2 });
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata> {
            [1] = new(1, 5, 50, 20, 100000028, 0.04f, 0.02f),
            [2] = new(2, 10, 50, 20, 100000029, 0.075f, 0.025f),
        }));
        test.Session.Player.Value.Account.PrestigeLevel = prestigeLevel;

        Buffs.RefreshPrestigeBuffs();
        Buffs.RefreshPrestigeBuffs();

        Assert.That(Held(), Is.EqualTo(held));
        Assert.That(held.Select(id => Only(id).Level), Is.EqualTo(levels));
    }

    // A prestige level up into the next step (55 for ability 1) puts the buff at that step in place of the held one.
    [Test]
    public void PrestigeAbilities_StepUpReplacesTheHeldStep() {
        Start(MapType.None, Effect(100000028, Plain), Effect(100000028, Plain) with { Level = 2 });
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata> {
            [1] = new(1, 5, 50, 20, 100000028, 0.04f, 0.02f),
        }));
        test.Session.Player.Value.Account.PrestigeLevel = 54;
        Buffs.RefreshPrestigeBuffs();
        Assert.That(Only(100000028).Level, Is.EqualTo(1));

        test.Session.Player.Value.Account.PrestigeLevel = 55;
        Buffs.RefreshPrestigeBuffs();
        Assert.That(Only(100000028).Level, Is.EqualTo(2));
    }

    // A field change gives the player a new actor object (FieldManager.SpawnPlayer: session.Buffs.ResetActor): the
    // buffs it held keep on with it as their holder and, for its own, as their caster; a buff another actor cast
    // keeps that caster.
    [Test]
    public void ResetActor_MovesTheHeldBuffsToTheNewActor() {
        Start(MapType.None, Effect(10000051, Plain), Effect(10000061, Plain));
        using var other = new FieldActor(Field, 77, Field.Metadata, null!);
        Add(10000051);
        Add(10000061, caster: other);
        using var moved = new FieldActor(Field, Player.ObjectId, Field.Metadata, null!);

        Buffs.ResetActor(moved);

        Assert.That(Only(10000051).Owner, Is.SameAs(moved));
        Assert.That(Only(10000051).Caster, Is.SameAs(moved));
        Assert.That(Only(10000061).Owner, Is.SameAs(moved));
        Assert.That(Only(10000061).Caster, Is.SameAs(other));
    }

    // A saved buff put back at login or after a server move (SetCacheBuffs) keeps its saved time left: the duration
    // invokes and the debuff reductions were taken when it was first added. KR 10400281 (the priest's, held: 10400182
    // +10000 ms) and a status debuff (44200001, 900 s) under 20% debuff reduction. A fresh add still takes both. Before,
    // each restore took them again (a restored 10400182 got 10 s more), whoever had cast the buff.
    [Test]
    public void ARestoredBuffKeepsItsTimeLeft() {
        Start(MapType.None,
            Effect(10400281, Plain with { DurationTick = 1_000_000 }) with {
                InvokeEffect = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseEffectDuration], [10000], [0], 10400182, 0, 0, 0),
            },
            Effect(10400182, Plain with { DurationTick = 20000 }),
            Effect(44200001, Plain with { DurationTick = 900000, Type = BuffType.Debuff, SubType = BuffSubType.Status }));
        Stat reduce = Player.Stats.Values[SpecialAttribute.ReduceDebuff];
        (reduce.Total, reduce.Current) = (200, 200);
        Add(10400281);

        Buffs.SetCacheBuffs([
            new BuffInfo { Id = 10400182, Level = 1, MsRemaining = 5000, Stacks = 1 },
            new BuffInfo { Id = 44200001, Level = 1, MsRemaining = 600000, Stacks = 1 },
        ], 2000);

        Assert.That(Only(10400182).EndTick, Is.EqualTo(7000));
        Assert.That(Only(44200001).EndTick, Is.EqualTo(602000));

        Buffs.Remove(10400182, Player.ObjectId);
        Buffs.Remove(44200001, Player.ObjectId);
        Add(10400182, tick: 3000);
        Add(44200001, tick: 3000);
        Assert.That(Only(10400182).EndTick, Is.EqualTo(3000 + 20000 + 10000), "a fresh add takes the invoke");
        Assert.That(Only(44200001).EndTick, Is.EqualTo(3000 + 720000), "and the reduction");
    }
}
