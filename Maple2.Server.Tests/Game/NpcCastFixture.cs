using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Microsoft.EntityFrameworkCore;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Tests.Game;

// A field with one npc that casts skills: the shared harness of the npc cast and death tests. The npc stands 1000 units
// from the test player, who is its battle target. Skills are built here with a motion per sequence name and the
// attack points of the motion's keys; the cast goes through MovementState (TryCastSkill, then StateSkillEvent for the
// keys the animation would send).
public abstract class NpcCastFixture : TriggerTestBase {
    protected const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    protected const int CasterId = 21000001;

    protected FieldNpc npc = null!;

    // The field's navigation: one flat mesh by default.
    protected virtual Navigation BuildNavigation() => TriggerHallGameTests.FlatNavigation();

    // The npc has the given sequences in its model; the skills are known to the field.
    protected FieldNpc StartCaster(string[] sequences, params SkillMetadata[] skills) => StartCaster(sequences, null, skills);

    // Keys: the named keys of a sequence (name -> key name and time in seconds); the sequence lasts 5 s.
    protected FieldNpc StartCaster(string[] sequences, IReadOnlyDictionary<string, (string Name, float Time)[]>? keys, params SkillMetadata[] skills) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, BuildNavigation());
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("<Pets>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPet>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));

        var storage = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(MetadataStorage<(int, short), SkillMetadata>).GetField("Cache", Any)!.GetValue(storage)!;
        foreach (SkillMetadata skill in skills) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(skill.Id, skill.Level), skill]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, storage);
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, AiStateTests.NpcTable(sequences, AiStateTests.Npc(CasterId)));
        if (keys != null) {
            var animations = new LRUCache<string, AnimationMetadata>(10, 1);
            animations.AddReplace("test_model", new AnimationMetadata("test_model", sequences.Select((name, i) => new AnimationSequenceMetadata(name, (short) (i + 1), 5,
                keys.TryGetValue(name, out (string Name, float Time)[]? named) ? named.Select(key => new AnimationKey(key.Name, key.Time)).ToList() : [])).ToDictionary(sequence => sequence.Name)));
            typeof(NpcMetadataStorage).GetField("AniCache", Any)!.SetValue(Field.NpcMetadata, animations);
        }
        typeof(FieldManager).GetField("<AiMetadata>k__BackingField", Any)!.SetValue(Field, AiStateTests.AiTable(new AiMetadata("AI_StateTest.xml", [], [], [], [])));
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(Field, 1_000_000L);

        Assert.That(Field.NpcMetadata.TryGet(CasterId, out NpcMetadata? metadata), Is.True);
        npc = Field.SpawnNpc(metadata!, new Vector3(1000, 0, 0), Vector3.Zero)!;
        test.Session.Player.Position = Vector3.Zero;
        npc.BattleState.ForceTarget(test.Session.Player);
        // The spawn state ends at the first movement update (no Regen_A in the rig): the npc then stands idle.
        npc.MovementState.Update(Field.FieldTick);
        test.TakeSent();
        return npc;
    }

    // A skill of one level: one motion per (sequence, attack point name) of the list; each motion's attack is a plain
    // hostile box of the given count of hits, named by its point.
    protected static SkillMetadata Skill(int id, params (string Sequence, string Point)[] motions) => Skill(id, 0f, motions);

    // The same with the motion's movedistance (the first motion's).
    protected static SkillMetadata Skill(int id, float moveDistance, params (string Sequence, string Point)[] motions) {
        SkillMetadataMotion[] built = motions.Select(motion => new SkillMetadataMotion(
            new SkillMetadataMotionProperty(motion.Sequence, 1f, moveDistance, false),
            string.IsNullOrEmpty(motion.Point) ? [] : [Attack(motion.Point)],
            string.IsNullOrEmpty(motion.Point) ? new Dictionary<string, byte>() : new Dictionary<string, byte> { [motion.Point] = 0 })).ToArray();
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var state = (SkillMetadataState) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataState));
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Motions = built,
            Skills = [],
        };
        return new SkillMetadata(id, 1, "test", property, state, level);
    }

    protected static SkillMetadataAttack Attack(string point) {
        var range = new SkillMetadataRange(SkillRegion.Box, 300, 150, 450, 0, 0, default, default, IncludeCasterType.Exclude,
            ApplyTargetType.Hostile, SkillTargetType.None);
        var arrow = new SkillMetadataArrow(Overlap: false, Explosion: false, RayPhysXTest: false, NonTarget: SkillTargetType.None,
            BounceType: BounceType.None, BounceCount: 0, BounceRadius: 0, BounceOverlap: false,
            Collision: default, CollisionAdd: default, RayType: 0);
        var damage = new SkillMetadataDamage(1, 1, 0, 0, false, 0, 0, false, null);
        return new SkillMetadataAttack(point, 0, 1, 0, 0, 0, 0, [], null, range, arrow, damage, [], []);
    }

    // The cast task of the skill on the npc, started and running (the harness's first task update).
    protected NpcTask Cast(int skillId) {
        NpcTask task = npc.MovementState.TryCastSkill(skillId, 1, 0, default, 0);
        npc.TaskState.Update(Field.FieldTick);
        return task;
    }

    protected int Sent(SendOp op, out List<byte[]> packets) {
        packets = test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).ToList();
        return packets.Count;
    }
}
