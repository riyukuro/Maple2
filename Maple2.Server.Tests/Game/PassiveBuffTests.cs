using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// Passive skill buffs follow the skill tree: a reset, an apply with fewer points or an applied tab with another
// tree takes off the passive buffs the new tree no longer gives at the same level.
public class PassiveBuffTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void OnlyPassivesTheNewTreeLacksOrChangesAreStale() {
        var held = new Dictionary<int, short> { { 10500021, 3 }, { 10500022, 2 }, { 10500023, 1 } };
        var wanted = new Dictionary<int, short> { { 10500021, 3 }, { 10500022, 1 }, { 10500024, 1 } };
        // Kept at level 3; lowered from 2 to 1; unlearned. The new one is not stale (nothing to take off).
        Assert.That(SkillBookRules.StalePassiveBuffs(held, wanted), Is.EquivalentTo(new[] { 10500022, 10500023 }));
        Assert.That(SkillBookRules.StalePassiveBuffs(held, new Dictionary<int, short>()), Is.EquivalentTo(held.Keys));
        Assert.That(SkillBookRules.StalePassiveBuffs(new Dictionary<int, short>(), wanted), Is.Empty);
    }

    [Test]
    public void AResetTakesTheHeldPassiveBuffsOff() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);

        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        foreach (int id in new[] { 10500021, 10500022, 10500023 }) {
            Buff buff = DisabledBuffTests.Held(DisabledBuffTests.Effect(id), true);
            typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, player);
            typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, player);
            buffs[id] = [buff];
        }

        var skills = (SkillManager) RuntimeHelpers.GetUninitializedObject(typeof(SkillManager));
        typeof(SkillManager).GetField("session", Any)!.SetValue(skills, test.Session);
        var applied = new Dictionary<int, short> { { 10500021, 1 }, { 10500022, 1 }, { 10500023, 1 } };
        typeof(SkillManager).GetField("passiveBuffs", Any)!.SetValue(skills, applied);

        // The tree keeps 10500021 at its level and gives up the other two.
        skills.RemoveStalePassives(new Dictionary<int, short> { { 10500021, 1 } });
        Assert.That(buffs.Where(entry => entry.Value.Count > 0).Select(entry => entry.Key), Is.EquivalentTo(new[] { 10500021 }));
        Assert.That(applied, Is.EquivalentTo(new Dictionary<int, short> { { 10500021, 1 } }));

        // A reset: nothing is wanted, so the last one comes off too.
        skills.RemoveStalePassives(new Dictionary<int, short>());
        Assert.That(buffs.Values.All(list => list.Count == 0), Is.True);
        Assert.That(applied, Is.Empty);
    }
}
