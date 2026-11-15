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
using Maple2.Server.Game.Util;
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

    // A box region 300 ahead (ImpactReach 1050), the fixture's attack.
    private static readonly SkillMetadataRange Box = new(SkillRegion.Box, 300, 150, 150, 0, 0, default, default,
        IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);
    private static readonly SkillMetadataArrow NoArrow = new(false, false, false, SkillTargetType.None, BounceType.None, 0, 0,
        false, default, default, 0);

    private static Scene Start(long magicPathId = 0, float moveDistance = 0, Vector3 shapeDimensions = default, params MagicPath[] paths) {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        foreach (string name in new[] { "fieldBreakables" }) {
            FieldInfo info = typeof(FieldManager).GetField(name, Any)!;
            info.SetValue(field, Activator.CreateInstance(info.FieldType));
        }
        typeof(FieldManager).GetField("triggerBreakable", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldBreakable>());
        FieldBreakable box = field.AddBreakable("box", new BreakableActor(true, 0, 1000, 1000, 0, Vector3.Zero, Vector3.Zero,
            MoveDistance: moveDistance, ShapeDimensions: shapeDimensions))!;

        // Attack point 0 has a brokenOffence, attack point 1 has none: the client makes a breakable area of 0 only.
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            BrokenOffence = 1, Range = Box, Arrow = NoArrow, MagicPathId = magicPathId,
        };
        var noBreak = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack));
        var motion = new SkillMetadataMotion(null!, [attack, noBreak], new Dictionary<string, byte>());
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Motions = [motion],
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var record = new SkillRecord(new SkillMetadata(SkillId, 1, null, property, null!, level), CastUid, test.Session.Player);
        record.Hits.Open(field.FieldTick);
        test.SetTable(new MagicPathTable(paths.Length == 0 ? new Dictionary<long, IReadOnlyList<MagicPath>>()
            : new Dictionary<long, IReadOnlyList<MagicPath>> { [magicPathId] = paths }));
        // The fixture's player is not constructed: it has no skill queue.
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ActiveSkills>k__BackingField", Any)!
            .SetValue(test.Session.Player, new SkillQueue());
        test.Session.Player.ActiveSkills.Add(record, field.FieldTick);
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
            scene.Test.Session.Player.ActiveSkills.Add(new SkillRecord(scene.Record.Metadata, uid, scene.Test.Session.Player),
                scene.Test.Session.Field!.FieldTick);
        }
        Assert.That(scene.Test.Session.Player.ActiveSkills.Get(CastUid), Is.Null, "no longer pending");
        Assert.That(Hit(scene), Is.EqualTo(BreakableState.Break));
    }

    // An attack point whose brokenOffence is 0 makes no breakable area in the client.
    [Test]
    public void AnAttackWithNoBrokenOffenceIsRefused() {
        Scene scene = Start();
        AttackPoint(scene, attackPoint: 1);
        Assert.That(Hit(scene, attackPoint: 1), Is.Not.EqualTo(BreakableState.Break));
    }

    // A motion or attack point the cast's skill level does not have makes no breakable area in the client: the KR
    // client has no breaking attack missing inside a skill level the server pack has.
    [TestCase((byte) 0, (byte) 2)]
    [TestCase((byte) 1, (byte) 0)]
    [TestCase((byte) 200, (byte) 0)]
    public void AnAttackPointTheLevelLacksIsRefused(byte motionPoint, byte attackPoint) {
        Scene scene = Start();
        AttackPoint(scene, attackPoint: attackPoint);
        Assert.That(Hit(scene, motionPoint: motionPoint, attackPoint: attackPoint), Is.Not.EqualTo(BreakableState.Break));
    }

    // A region of a skill level the server's skill data lacks may be one the client has (the KR client's breaking
    // attacks of the skill levels the server pack lacks): taken, not refused.
    [Test]
    public void ARegionOfALevelTheDataLacksBreaks() {
        Scene scene = Start();
        FieldManager field = scene.Test.Session.Field!;
        var skills = new ConcurrentDictionary<int, FieldSkill>();
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        skills[900] = new FieldSkill(field, 900, scene.Test.Session.Player, scene.Record.Metadata with { Level = 3 }, 1000, Vector3.Zero);
        Assert.That(Hit(scene, 0, SkillId, 3), Is.EqualTo(BreakableState.Break));
    }

    private static void SetTick(FieldManager field, long tick) {
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, tick);
    }

    // A cast whose magic path lives 15 s (keep time 5 + 15 + 2 s), then 40 fast casts, 0.3 s apart: the magic still
    // breaks the box 15 s after its cast, however many casts came after it; past its keep time it no longer does.
    [TestCase(15000, ExpectedResult = true)]
    [TestCase(23000, ExpectedResult = false)]
    public bool ALongLivedMagicOutlivesFortyCasts(long sinceCast) {
        Scene scene = Start();
        FieldManager field = scene.Test.Session.Field!;
        SkillQueue queue = scene.Test.Session.Player.ActiveSkills;
        long start = field.FieldTick;
        var path = (MagicPath) RuntimeHelpers.GetUninitializedObject(typeof(MagicPath)) with { LifeTime = 15 };
        SkillRecord fixture = scene.Record;
        SkillMetadata metadata = fixture.Metadata with {
            Data = fixture.Metadata.Data with {
                Motions = [fixture.Motion with { Attacks = [fixture.Attack with { MagicPathId = 5 }] }],
            },
        };
        long keepMs = SkillHitRules.CastKeepMs(metadata, new Dictionary<long, IReadOnlyList<MagicPath>> { [5] = [path] });
        Assert.That(keepMs, Is.EqualTo(22000));

        var magic = new SkillRecord(metadata, 500, scene.Test.Session.Player);
        magic.Hits.Open(start);
        queue.Add(magic, start, keepMs);
        Assert.That(magic.Hits.TakePoint(0, 0, held: false, count: 1, start), Is.True);
        magic.Hits.Declare(2000, 0, 0);
        for (int i = 1; i <= 40; i++) {
            long tick = start + 300 * i;
            var cast = new SkillRecord(fixture.Metadata, 500 + i, scene.Test.Session.Player);
            cast.Hits.Open(tick);
            queue.Add(cast, tick, SkillHitRules.CastKeepMs(fixture.Metadata, null));
        }

        SetTick(field, start + sinceCast);
        return Hit(scene, 500) == BreakableState.Break;
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

    // The tag's area met the box, so the box lies within the attack's reach (SkillHitRules.BreakableTooFar): a box
    // region 300 ahead reaches 1050 from the cast, and the box's own size is taken as BreakableMargin (600) more.
    // Before, a cast broke a box anywhere on the map.
    [TestCase(1500f, ExpectedResult = true)]
    [TestCase(1600f, ExpectedResult = true)]
    [TestCase(5000f, ExpectedResult = false)]
    public bool ABoxPastTheAttacksReachIsRefused(float away) {
        Scene scene = Start();
        scene.Record.CastPosition = new Vector3(away, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(away, 0, 0);
        AttackPoint(scene);
        return Hit(scene) == BreakableState.Break;
    }

    // A box that moves 3000 along its path (moveDistance), hit at the far end 4500 from where it was placed: the
    // client tests the area against the moved box, so it breaks; past its path and the attack's reach it does not.
    [TestCase(4500f, ExpectedResult = true)]
    [TestCase(8000f, ExpectedResult = false)]
    public bool AMovingBoxHitAtItsPathsFarEndBreaks(float away) {
        Scene scene = Start(moveDistance: 3000);
        scene.Record.CastPosition = new Vector3(away, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(away, 0, 0);
        AttackPoint(scene);
        return Hit(scene) == BreakableState.Break;
    }

    // A large box (ShapeDimensions 500 each way) is hit at its side, past the margin from its position: it breaks.
    [TestCase(2400f, ExpectedResult = true)]
    [TestCase(5000f, ExpectedResult = false)]
    public bool ALargeBoxHitAtItsSideBreaks(float away) {
        Scene scene = Start(shapeDimensions: new Vector3(500, 500, 500));
        scene.Record.CastPosition = new Vector3(away, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(away, 0, 0);
        AttackPoint(scene);
        return Hit(scene) == BreakableState.Break;
    }

    // A held cast or a dash moved the player since the cast: the box near the player now breaks.
    [Test]
    public void ABoxNearThePlayerNowBreaks() {
        Scene scene = Start();
        scene.Record.CastPosition = new Vector3(8000, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(500, 0, 0);
        AttackPoint(scene);
        Assert.That(Hit(scene), Is.EqualTo(BreakableState.Break));
    }

    // An arrow's magic flies its path's distance: a box 3500 away breaks with a 3000 path (3000 + 150 + 600), not
    // 5000 away. A path from a fixed point has no reach from the cast and is not checked.
    [TestCase(3500f, false, ExpectedResult = true)]
    [TestCase(5000f, false, ExpectedResult = false)]
    [TestCase(50000f, true, ExpectedResult = true)]
    public bool AnArrowsPathReachesTheBox(float away, bool fixedPoint) {
        var path = (MagicPath) RuntimeHelpers.GetUninitializedObject(typeof(MagicPath)) with {
            Distance = 3000, FireFixed = fixedPoint ? new Vector3(0, 0, 100) : Vector3.Zero,
        };
        Scene scene = Start(5, paths: [path]);
        scene.Record.CastPosition = new Vector3(away, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(away, 0, 0);
        AttackPoint(scene);
        return Hit(scene) == BreakableState.Break;
    }
}
