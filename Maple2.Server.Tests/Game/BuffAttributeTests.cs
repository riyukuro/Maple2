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
}
