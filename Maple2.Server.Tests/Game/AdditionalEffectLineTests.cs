using System;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// additionaleffect_95000012 and _95000014 (117, 118): a held line gives its passive effect at the line's level.
public class AdditionalEffectLineTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldPlayer Player => test.Session.Player;

    private const int Coat = 11400001;

    // A session whose field knows both line effects at levels 1 to 20, as KR has them. With a coat, the session
    // keeps one in the bag and runs whole stat rebuilds (equip, unequip, revive); healthOn14 gives 95000014 a status.
    private void Start(bool coat = false, long healthOn14 = 0) {
        test = new TestGameSession();
        long coatUid = 0;
        if (coat) {
            test.SetTable(new SetItemTable(new System.Collections.Generic.Dictionary<int, SetItemTable.Entry>()));
            coatUid = test.Store(TestGameSession.Metadata(Coat, EquipSlot.CL, slotMax: 1)).Uid;
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        FieldManager field = test.Session.Field!;
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (int id in new[] { 95000012, 95000014 }) {
            for (short level = 1; level <= 20; level++) {
                AdditionalEffectMetadata effect = BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { DurationTick = 3_600_000 }) with { Level = level };
                if (id == 95000014 && healthOn14 > 0) {
                    effect = effect with { Status = effect.Status with { Values = new System.Collections.Generic.Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = healthOn14 } } };
                }
                cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, level), effect]);
            }
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, field);
        if (!coat) {
            // The fixture builds its stats stub without a constructor, so the given-effects map starts unset.
            typeof(StatsManager).GetField("lineEffects", Any)!.SetValue(Player.Stats, new System.Collections.Generic.Dictionary<int, short>());
            return;
        }

        // A whole rebuild: a ride manager, a level 1000 HP row for the character's job and level, and the config
        // parts the stat allocation reads.
        GameSession session = test.Session;
        session.Ride = new RideManager(session);
        Character character = Player.Value.Character;
        var levels = new System.Collections.Generic.Dictionary<short, System.Collections.Generic.IReadOnlyDictionary<BasicAttribute, long>> {
            [character.Level] = new System.Collections.Generic.Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1000 },
        };
        session.Stats = new StatsManager(Player, new UserStatTable(
            new System.Collections.Generic.Dictionary<JobCode, System.Collections.Generic.IReadOnlyDictionary<short, System.Collections.Generic.IReadOnlyDictionary<BasicAttribute, long>>> {
                [character.Job.Code()] = levels,
            }));
        typeof(ConfigManager).GetField("session", Any)!.SetValue(session.Config, session);
        typeof(ConfigManager).GetField("statAttributes", Any)!.SetValue(session.Config,
            new Maple2.Server.Game.Manager.Config.StatAttributes(new System.Collections.Generic.Dictionary<string, int>()));
        CoatUid = coatUid;
    }

    private long CoatUid;

    // The bag's coat with an additional effect line of the given total.
    private void Line(SpecialAttribute line, int total) {
        Item item = test.Session.Item.Inventory.Get(CoatUid)!;
        item.Stats ??= new ItemStats();
        item.Stats[ItemStats.Type.Random].Special[line] = new SpecialOption(total);
    }

    private void Equip() => Assert.That(test.Session.Item.Equips.Equip(CoatUid, EquipSlot.CL, isSkin: false), Is.True);

    private void Unequip() => Assert.That(test.Session.Item.Equips.Unequip(CoatUid), Is.True);

    private long Health => Player.Stats.Values[BasicAttribute.Health].Total;

    private short[] Levels(int id) => Player.Buffs.EnumerateBuffs(id).Select(buff => buff.Level).ToArray();

    private void Hold(SpecialAttribute line, long total) {
        Stats stats = Player.Stats.Values;
        stats[line].AddTotal(total - stats[line].Total);
        Player.Stats.RefreshLineEffects(Player);
    }

    [TestCase(5, 5)]
    [TestCase(20, 20)]
    [TestCase(25, 20)]
    [TestCase(0, 0)]
    [TestCase(-3, 0)]
    public void Level(long total, short level) {
        Assert.That(AdditionalEffectLineRules.Level(total, at => at <= 20), Is.EqualTo(level));
    }

    [Test]
    public void HeldLineGivesItsEffectAtItsLevel() {
        Start();

        Hold(SpecialAttribute.AdditionalEffect95000012, 4);

        Assert.That(Levels(95000012), Is.EqualTo(new short[] { 4 }));
        Assert.That(Levels(95000014), Is.Empty);
    }

    [Test]
    public void ChangedLineReplacesTheLevelAndADroppedLineTakesTheEffect() {
        Start();
        Hold(SpecialAttribute.AdditionalEffect95000014, 3);

        Hold(SpecialAttribute.AdditionalEffect95000014, 7);
        Assert.That(Levels(95000014), Is.EqualTo(new short[] { 7 }));

        Hold(SpecialAttribute.AdditionalEffect95000014, 0);
        Assert.That(Levels(95000014), Is.Empty);
    }

    // An effect lost while the line stays (a death clears it) comes back at the next rebuild.
    [Test]
    public void LostEffectComesBack() {
        Start();
        Hold(SpecialAttribute.AdditionalEffect95000012, 2);
        Player.Buffs.Remove(95000012, Player.ObjectId);

        Player.Stats.RefreshLineEffects(Player);

        Assert.That(Levels(95000012), Is.EqualTo(new short[] { 2 }));
    }

    // The equip's rebuild counts the coat's line and gives the effect at its level; the unequip's takes it.
    [Test]
    public void EquipGivesTheLineEffectAndUnequipTakesIt() {
        Start(coat: true);
        Line(SpecialAttribute.AdditionalEffect95000012, 4);

        Equip();
        Assert.That(Levels(95000012), Is.EqualTo(new short[] { 4 }));

        Unequip();
        Assert.That(Levels(95000012), Is.Empty);
    }

    // A death takes the effect (it does not keep on death); the revive's rebuild gives it again while the coat stays on.
    [Test]
    public void DeathTakesTheEffectAndTheNextRebuildGivesItBack() {
        Start(coat: true);
        Line(SpecialAttribute.AdditionalEffect95000014, 6);
        Equip();

        Player.Buffs.OnDeath();
        Assert.That(Levels(95000014), Is.Empty);

        Player.Stats.Refresh();
        Assert.That(Levels(95000014), Is.EqualTo(new short[] { 6 }));
    }

    // An effect with a status (none in KR): it is given after the equip's rebuild is sent, so its own rebuild comes
    // second and is not run inside the first (whose stats packet would then go out after it); the unequip takes
    // it and the health with it.
    [Test]
    public void LineEffectWithAStatusRebuildsAfterTheRebuildThatGaveIt() {
        Start(coat: true, healthOn14: 100);
        Player.Stats.Refresh();
        long bare = Health;
        Line(SpecialAttribute.AdditionalEffect95000014, 2);
        var sentTotals = new System.Collections.Generic.List<long>();
        test.OnSend = packet => {
            if (TestGameSession.Op(packet) == SendOp.Stat) {
                sentTotals.Add(Health);
            }
        };

        Equip();
        test.OnSend = null;
        Assert.That(Levels(95000014), Is.EqualTo(new short[] { 2 }));
        Assert.That(Health, Is.EqualTo(bare + 100));
        Assert.That(sentTotals, Is.EqualTo(new[] { bare, bare + 100 }));

        Unequip();
        Assert.That(Levels(95000014), Is.Empty);
        Assert.That(Health, Is.EqualTo(bare));
    }
}
