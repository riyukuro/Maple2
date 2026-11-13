using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game;

// A skill range's target choice (targetSelectType with targetHasBuffID / targetHasBuffOwner / targetSelectParty /
// targetSelectGuild, and targetStatCompare / targetStatCompareFunction), applied to the whole pool before the target
// count is taken: 90000546 heals only the caster's party, 10600031 hits the targets with the caster's 10600151 first,
// 10400153 heals the ally with the lowest HP rate first.
public class RangeTargetSelectTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Scene {
        public required FieldManager Field;
        public required FieldPlayer A;
        public required FieldPlayer B;
        public required FieldPlayer C;
    }

    // A at the origin, B and C (other players, friendly to A) near it.
    private static Scene Start() {
        var a = new TestGameSession();
        a.Start();
        var tb = new TestGameSession(a.Options);
        tb.Start();
        var tc = new TestGameSession(a.Options);
        tc.Start();
        FieldManager field = a.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        int objectId = 2;
        foreach ((TestGameSession other, float x) in new[] { (tb, 100f), (tc, 200f) }) {
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, objectId);
            field.Players[objectId++] = other.Session.Player;
            other.Session.Field = field;
            other.Session.Player.Position = new Vector3(x, 0, 0);
        }
        a.Session.Player.Position = Vector3.Zero;
        foreach (TestGameSession test in new[] { a, tb, tc }) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, field);
            test.Session.Player.Value.Character.PartyId = 0;
            test.Session.Player.Value.Character.GuildId = 0;
        }
        return new Scene { Field = field, A = a.Session.Player, B = tb.Session.Player, C = tc.Session.Player };
    }

    private static SkillMetadataRange Range(SkillMetadataRangeSelect? select, IncludeCasterType includeCaster = IncludeCasterType.Exclude) {
        return new SkillMetadataRange(SkillRegion.Cylinder, 600, 150, 0, 0, 0, Vector3.Zero, Vector3.Zero, includeCaster,
            ApplyTargetType.Friendly, SkillTargetType.None, select);
    }

    private static SkillMetadataRangeSelect Select(int mode, int buffId = 0, bool fromCaster = false, bool party = false,
            bool guild = false, int statSort = 0) {
        return new SkillMetadataRangeSelect(mode, buffId, fromCaster, party, guild, BasicAttribute.Health, statSort);
    }

    private static readonly Prism[] Around = [new Prism(new Circle(Vector2.Zero, 600), -150, 300)];

    private static int[] Targets(Scene scene, SkillMetadataRange range, int count) {
        return scene.Field.GetTargets(scene.A, Around, range, count).Select(target => target.ObjectId).ToArray();
    }

    // The buff 10600151 on the target, cast by the given actor.
    private static void Mark(IActor target, IActor caster, bool enabled = true) {
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(target.Buffs)!;
        Buff buff = DisabledBuffTests.Held(DisabledBuffTests.Effect(10600151), enabled);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, target);
        buffs[10600151] = [buff];
    }

    private static void Health(FieldPlayer player, long current, long total) {
        Stat health = player.Stats.Values[BasicAttribute.Health];
        health.Total = total;
        health.Current = current;
    }

    // Mode 2 with the party test: only the caster's party members, and no one at all for a caster with no party
    // (party 0 matches no one). The caster still comes first when the range includes it.
    [Test]
    public void PartyOnly_KeepsTheCastersParty() {
        Scene scene = Start();
        Assert.That(Targets(scene, Range(null), 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));
        Assert.That(Targets(scene, Range(Select(2, party: true)), 5), Is.Empty);

        scene.A.Value.Character.PartyId = 7;
        scene.B.Value.Character.PartyId = 7;
        scene.C.Value.Character.PartyId = 8;
        Assert.Multiple(() => {
            Assert.That(Targets(scene, Range(Select(2, party: true)), 5), Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(2, party: true), IncludeCasterType.Priority), 5),
                Is.EqualTo(new[] { scene.A.ObjectId, scene.B.ObjectId }));
        });
    }

    // The guild test: the same non-zero guild.
    [Test]
    public void GuildOnly_KeepsTheCastersGuild() {
        Scene scene = Start();
        scene.A.Value.Character.GuildId = 30;
        scene.C.Value.Character.GuildId = 30;
        Assert.That(Targets(scene, Range(Select(2, guild: true)), 5), Is.EqualTo(new[] { scene.C.ObjectId }));
    }

    // Mode 3 with the caster's mark: the marked targets if there are any (the count is taken from them), else all.
    // Another caster's mark or a disabled one does not count; without targetHasBuffOwner anyone's does.
    [Test]
    public void MarkedFirst_FallsBackToAll() {
        Scene scene = Start();
        SkillMetadataRange own = Range(Select(3, 10600151, fromCaster: true));
        Assert.That(Targets(scene, own, 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));

        Mark(scene.C, scene.A);
        Assert.Multiple(() => {
            Assert.That(Targets(scene, own, 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, own, 5), Is.EqualTo(new[] { scene.C.ObjectId }));
        });

        Mark(scene.C, scene.B);
        Assert.Multiple(() => {
            Assert.That(Targets(scene, own, 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(3, 10600151)), 5), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(2, 10600151)), 5), Is.EqualTo(new[] { scene.C.ObjectId }));
        });

        Mark(scene.C, scene.A, enabled: false);
        Assert.That(Targets(scene, Range(Select(2, 10600151, fromCaster: true)), 5), Is.Empty);
    }

    // The stat sort: 3 picks the lowest HP rate first, 1 the lowest current HP, 2 and 4 the highest; with a count of 1
    // only the first is taken.
    [Test]
    public void StatSort_OrdersByHealth() {
        Scene scene = Start();
        Health(scene.B, 3000, 5000); // 60 %, 3000
        Health(scene.C, 1000, 1000); // 100 %, 1000
        Assert.Multiple(() => {
            Assert.That(Targets(scene, Range(Select(0, statSort: 3)), 1), Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 4)), 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 1)), 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 2)), 1), Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 3)), 5), Is.EqualTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));
        });
    }

    // Modes 1 and 4 filter nothing; a select with neither a filter nor a sort is inactive.
    [Test]
    public void OtherModes_FilterNothing() {
        Scene scene = Start();
        Assert.Multiple(() => {
            Assert.That(Targets(scene, Range(Select(1, party: true)), 5), Has.Length.EqualTo(2));
            Assert.That(Targets(scene, Range(Select(4, 10600151)), 5), Has.Length.EqualTo(2));
            Assert.That(Select(1, 10600151).Active(), Is.False);
            Assert.That(Select(3).Active(), Is.True);
            Assert.That(Select(0, statSort: 3).Active(), Is.True);
        });
    }
}
