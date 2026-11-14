using System;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// additionaleffect_95000012 and _95000014 (117, 118): a held line gives its passive effect at the line's level.
public class AdditionalEffectLineTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldPlayer Player => test.Session.Player;

    // A session whose field knows both line effects at levels 1 to 20, as KR has them.
    private void Start() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        FieldManager field = test.Session.Field!;
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (int id in new[] { 95000012, 95000014 }) {
            for (short level = 1; level <= 20; level++) {
                AdditionalEffectMetadata effect = BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { DurationTick = 3_600_000 }) with { Level = level };
                cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, level), effect]);
            }
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, field);
        // The fixture builds its stats stub without a constructor, so the given-effects map starts unset.
        typeof(StatsManager).GetField("lineEffects", Any)!.SetValue(Player.Stats, new System.Collections.Generic.Dictionary<int, short>());
    }

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
}
