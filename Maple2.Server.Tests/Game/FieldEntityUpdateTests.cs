using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// One entity whose update throws no longer skips the entities after it in that tick: each is updated on its own and
// the thrower is logged. Before, the throw left Update and the rest of the field waited for the next tick.
public class FieldEntityUpdateTests {
    private sealed class ThrowingNpc : FieldNpc {
        public ThrowingNpc() : base(null!, 0, null, null!, "") { }
        public override void Update(long tickCount) => throw new InvalidOperationException("test");
    }

    private sealed class CountingNpc : FieldNpc {
        public CountingNpc() : base(null!, 0, null, null!, "") { }
        public int Updates;
        public override void Update(long tickCount) => Updates++;
    }

    private static T Make<T>() => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Test]
    public void AThrowingNpc_LeavesTheOthersUpdating() {
        var test = new TestGameSession();
        FieldManager field = test.Start().Field!;
        var after = Make<CountingNpc>();
        var mob = Make<CountingNpc>();
        field.Npcs[1] = Make<ThrowingNpc>();
        field.Npcs[2] = after;
        // The test field is made without its constructor: give it the empty tables and queues the update reads.
        for (Type? type = typeof(FieldManager); type != null; type = type.BaseType) {
            foreach (FieldInfo info in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                Type fieldType = info.FieldType;
                if (info.GetValue(field) == null && fieldType.Namespace?.StartsWith("System.Collections") == true
                    && !fieldType.IsAbstract && !fieldType.IsInterface && fieldType.GetConstructor(Type.EmptyTypes) != null) {
                    info.SetValue(field, Activator.CreateInstance(fieldType));
                }
            }
        }
        field.Mobs[3] = mob;

        // The bare field throws later in its update (after the entities); RunUpdate catches that as before.
        Assert.DoesNotThrow(field.RunUpdate);
        Assert.DoesNotThrow(field.RunUpdate);
        Assert.Multiple(() => {
            Assert.That(after.Updates, Is.EqualTo(2), "the npc after the thrower");
            Assert.That(mob.Updates, Is.EqualTo(2), "the mobs, updated after the npcs");
        });
    }
}
