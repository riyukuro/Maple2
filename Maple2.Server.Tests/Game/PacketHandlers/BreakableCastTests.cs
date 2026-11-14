using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Breakable 0x27: the client sends it from a tag of one of the player's own casts, made with the cast's id when the
// cast's attack point fires and sends its packet (stateskill.cpp:1348-1370, worldservice_1.cpp:9302). The server
// made a new record from whatever skill the packet named, so any uid broke the object, dropped its box and counted.
public class BreakableCastTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long CastUid = 77;
    private const int SkillId = 10000001;

    private sealed class Scene {
        public required TestGameSession Test;
        public required SkillRecord Record;
        public required FieldBreakable Box;
        public required SkillMetadataStorage Storage;
    }

    private static Scene Start() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        foreach (string name in new[] { "fieldBreakables" }) {
            FieldInfo info = typeof(FieldManager).GetField(name, Any)!;
            info.SetValue(field, Activator.CreateInstance(info.FieldType));
        }
        typeof(FieldManager).GetField("triggerBreakable", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldBreakable>());
        FieldBreakable box = field.AddBreakable("box", new BreakableActor(true, 0, 1000, 1000, 0, Vector3.Zero, Vector3.Zero))!;

        // Attack point 0 has a brokenOffence, attack point 1 has none: the client makes a breakable area of 0 only.
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with { BrokenOffence = 1 };
        var noBreak = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack));
        var motion = new SkillMetadataMotion(null!, [attack, noBreak], new Dictionary<string, byte>());
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Motions = [motion],
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var record = new SkillRecord(new SkillMetadata(SkillId, 1, null, property, null!, level), CastUid, test.Session.Player);
        record.Hits.Open(field.FieldTick);
        // The fixture's player is not constructed: it has no skill queue.
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ActiveSkills>k__BackingField", Any)!
            .SetValue(test.Session.Player, new SkillQueue());
        test.Session.Player.ActiveSkills.Add(record);
        // The skill table holds the cast's skill and the other one the cases name (the handler before read it).
        var storage = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(storage)!;
        foreach ((int id, short lv) in new[] { (SkillId, (short) 1), (SkillId, (short) 2), (10000002, (short) 1) }) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, lv), record.Metadata with { Id = id, Level = lv }]);
        }
        return new Scene { Test = test, Record = record, Box = box, Storage = storage };
    }

    // The attack point's packet, declaring tags attacks.
    private static void AttackPoint(Scene scene, int tags = 1, byte attackPoint = 0) {
        Assert.That(scene.Record.Hits.TakePoint(attackPoint, 0, held: false, count: tags, scene.Test.Session.Field!.FieldTick), Is.True);
        for (int i = 0; i < tags; i++) {
            scene.Record.Hits.Declare(1000 + i, attackPoint, 0);
        }
    }

    private static BreakableState Hit(Scene scene, long castUid = CastUid, int skillId = SkillId, short level = 1,
                                      byte motionPoint = 0, byte attackPoint = 0) {
        new BreakableHandler { SkillMetadata = scene.Storage }.Handle(scene.Test.Session, TestGameSession.Packet(writer => {
            writer.WriteString("box");
            writer.WriteLong(castUid);
            writer.WriteInt(skillId);
            writer.WriteShort(level);
            writer.WriteByte(motionPoint);
            writer.WriteByte(attackPoint);
        }));
        return scene.Box.State;
    }

    [Test]
    public void TheCastsTagBreaksTheBox() {
        Scene scene = Start();
        Assert.That(Hit(scene), Is.Not.EqualTo(BreakableState.Break), "before the attack point's packet");
        AttackPoint(scene);
        Assert.That(Hit(scene), Is.EqualTo(BreakableState.Break));
    }

    [Test]
    public void NoTagNoBreak() {
        Scene scene = Start();
        AttackPoint(scene, tags: 0);
        Assert.That(Hit(scene), Is.Not.EqualTo(BreakableState.Break));
    }

    // A region skill's tags carry uid 0 and the region's skill (scenestategame.cpp:5567): taken while the player's
    // region of that skill and level is in the field.
    [TestCase(true, SkillId, (short) 1, ExpectedResult = true)]
    [TestCase(false, SkillId, (short) 1, ExpectedResult = false)]
    [TestCase(true, 10000002, (short) 1, ExpectedResult = false)]
    [TestCase(true, SkillId, (short) 2, ExpectedResult = false)]
    public bool ARegionOfThePlayerBreaksTheBox(bool ownRegion, int skillId, short level) {
        Scene scene = Start();
        FieldManager field = scene.Test.Session.Field!;
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldSkill>());
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        if (ownRegion) {
            var region = new FieldSkill(field, 900, scene.Test.Session.Player, scene.Record.Metadata, 1000, Vector3.Zero);
            ((ConcurrentDictionary<int, FieldSkill>) typeof(FieldManager).GetField("fieldSkills", Any)!.GetValue(field)!)[900] = region;
        }
        return Hit(scene, 0, skillId, level) == BreakableState.Break;
    }

    // Nearly every region is removed on the first tick after its last fire, before the client's tags of that fire
    // meet anything: a removed region of the player still breaks within the hit window, not after it.
    [TestCase(1000, ExpectedResult = true)]
    [TestCase(6000, ExpectedResult = false)]
    public bool ARegionJustRemovedStillBreaksTheBox(long sinceRemoved) {
        Scene scene = Start();
        FieldManager field = scene.Test.Session.Field!;
        var skills = new ConcurrentDictionary<int, FieldSkill>();
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        skills[900] = new FieldSkill(field, 900, scene.Test.Session.Player, scene.Record.Metadata, 1000, Vector3.Zero);
        long tick = field.FieldTick;
        field.RemoveSkill(900);
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, tick + sinceRemoved);
        return Hit(scene, 0) == BreakableState.Break;
    }

    [TestCase(78, SkillId, (short) 1)]
    [TestCase(CastUid, 10000002, (short) 1)]
    [TestCase(CastUid, SkillId, (short) 2)]
    public void AnotherCastOrSkillIsRefused(long castUid, int skillId, short level) {
        Scene scene = Start();
        AttackPoint(scene);
        Assert.That(Hit(scene, castUid, skillId, level), Is.Not.EqualTo(BreakableState.Break));
    }

    // The player cast three more skills before the cast's magic met the box: the pending queue dropped the cast, and
    // the client still sends the hit with its uid, so the box breaks.
    [Test]
    public void ACastPushedOutOfThePendingQueueStillBreaks() {
        Scene scene = Start();
        AttackPoint(scene);
        for (long uid = CastUid + 1; uid <= CastUid + 3; uid++) {
            scene.Test.Session.Player.ActiveSkills.Add(new SkillRecord(scene.Record.Metadata, uid, scene.Test.Session.Player));
        }
        Assert.That(scene.Test.Session.Player.ActiveSkills.Get(CastUid), Is.Null, "no longer pending");
        Assert.That(Hit(scene), Is.EqualTo(BreakableState.Break));
    }

    // An attack point whose brokenOffence is 0, or one the skill does not have, makes no breakable area in the client.
    [TestCase((byte) 0, (byte) 1)]
    [TestCase((byte) 0, (byte) 2)]
    [TestCase((byte) 1, (byte) 0)]
    public void AnAttackWithNoBrokenOffenceIsRefused(byte motionPoint, byte attackPoint) {
        Scene scene = Start();
        AttackPoint(scene, attackPoint: attackPoint);
        Assert.That(Hit(scene, motionPoint: motionPoint, attackPoint: attackPoint), Is.Not.EqualTo(BreakableState.Break));
    }

    // A region's hit is held to the same rule: its attack point 1 has no brokenOffence.
    [TestCase((byte) 0, ExpectedResult = true)]
    [TestCase((byte) 1, ExpectedResult = false)]
    public bool ARegionsAttackNeedsABrokenOffence(byte attackPoint) {
        Scene scene = Start();
        FieldManager field = scene.Test.Session.Field!;
        var skills = new ConcurrentDictionary<int, FieldSkill>();
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        skills[900] = new FieldSkill(field, 900, scene.Test.Session.Player, scene.Record.Metadata, 1000, Vector3.Zero);
        return Hit(scene, 0, attackPoint: attackPoint) == BreakableState.Break;
    }
}
