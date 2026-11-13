using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The combat s2c layouts, field by field as the client reads them (CSceneStateGame in scenestategame.cpp). Region and
// Tile damage are in RegionDamagePacketTests, SkillUseFailed in SkillUseFailedLayoutTests.
public class CombatLayoutTests {
    private static FieldPlayer Player() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        return test.Session.Player;
    }

    private static ByteReader Reader(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(op));
        return reader;
    }

    private static SkillMetadata Skill(int id, short level) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        return new SkillMetadata(id, level, null, property, null!, null!);
    }

    private static SkillRecord Cast(FieldPlayer caster) => new(Skill(10200001, 3), 9_000_000_001, caster) {
        ServerTick = 123_456,
        Position = new Vector3(100, -200, 300),
        Direction = new Vector3(0, 1, 0),
        Rotation = new Vector3(0, 0, 90),
        Rotate2Z = 1.5f,
    };

    // Mode 0 (OnSkillDamage :5513): long cast uid, int caster, int skill, short level, byte motion, byte attack,
    // short point, float direction, bool, byte n; per target long, long, int, byte, byte.
    [Test]
    public void DamageTarget() {
        FieldPlayer caster = Player();
        var target = new TargetRecord { PrevUid = 11, Uid = 22, TargetId = 33, Unknown = 4, Index = 5 };
        ByteReader reader = Reader(SkillDamagePacket.Target(Cast(caster), [target]), SendOp.SkillDamage);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(9_000_000_001));
        Assert.That(reader.ReadInt(), Is.EqualTo(caster.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(10200001));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That((reader.ReadShort(), reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 100, (short) -200, (short) 300)));
        Assert.That((reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((0f, 1f, 0f)));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadInt(), Is.EqualTo(33));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.Available, Is.Zero);
    }

    // Mode 1 (:5609): long, long, int, int, short, byte, byte, two short points, byte n; per target int, byte hits,
    // hits x (byte type, long amount).
    [Test]
    public void DamageHits() {
        FieldPlayer caster = Player();
        FieldPlayer hit = Player();
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 1,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        var record = new DamageRecord(new SkillMetadata(10200001, 3, null, property, null!, null!), attack) {
            SkillUid = 9_000_000_001,
            TargetUid = 9_000_000_002,
            CasterId = caster.ObjectId,
            SkillId = 10200001,
            Level = 3,
            MotionPoint = 1,
            AttackPoint = 2,
            Position = new Vector3(10, 20, 30),
            Direction = new Vector3(0, 1, 0),
        };
        var entry = new DamageRecordTarget(hit);
        entry.AddDamage(DamageType.Critical, 1234);
        entry.AddDamage(DamageType.Normal, 56);
        record.Targets[hit.ObjectId] = entry;

        ByteReader reader = Reader(SkillDamagePacket.Damage(record), SendOp.SkillDamage);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(9_000_000_001));
        Assert.That(reader.ReadLong(), Is.EqualTo(9_000_000_002));
        Assert.That(reader.ReadInt(), Is.EqualTo(caster.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(10200001));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That((reader.ReadShort(), reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 10, (short) 20, (short) 30)));
        Assert.That((reader.ReadShort(), reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 0, (short) 1, (short) 0)));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(hit.ObjectId));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DamageType.Critical));
        Assert.That(reader.ReadLong(), Is.EqualTo(1234));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DamageType.Normal));
        Assert.That(reader.ReadLong(), Is.EqualTo(56));
        Assert.That(reader.Available, Is.Zero);
    }

    // Mode 3 (OnDotDamage :5663): int caster, int target, int buff object id, byte type, long amount.
    [Test]
    public void DotDamage() {
        FieldPlayer caster = Player();
        FieldPlayer target = Player();
        var dot = new AdditionalEffectMetadataDot.DotDamage(AttackType.None, Element.None, UseGrade: false, Rate: 0,
            HpValue: 300, SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: true,
            NotKill: false);
        var record = new DotDamageRecord(caster, target, dot) { BuffObjectId = 4321 };

        ByteReader reader = Reader(SkillDamagePacket.DotDamage(record), SendOp.SkillDamage);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(caster.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(target.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(4321));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) record.Type));
        Assert.That(reader.ReadLong(), Is.EqualTo(record.HpAmount));
        Assert.That(reader.Available, Is.Zero);
    }

    // Mode 4 (FUN_14171d820 :5685): six ints (caster, target, owner, hp, sp, ep), then byte animate.
    [TestCase(true)]
    [TestCase(false)]
    public void Heal(bool animate) {
        FieldPlayer caster = Player();
        FieldPlayer target = Player();
        var recovery = new AdditionalEffectMetadataRecovery(RecoveryRate: 0, HpValue: 1000, HpRate: 0, HpConsumeRate: 0,
            SpValue: 100, SpRate: 0, SpConsumeRate: 0, EpValue: 50, EpRate: 0, DisableCrit: true);
        var record = new HealDamageRecord(caster, target, 77, recovery);

        ByteReader reader = Reader(SkillDamagePacket.Heal(record, animate), SendOp.SkillDamage);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(caster.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(target.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(1000));
        Assert.That(reader.ReadInt(), Is.EqualTo(100));
        Assert.That(reader.ReadInt(), Is.EqualTo(50));
        Assert.That(reader.ReadBool(), Is.EqualTo(animate));
        Assert.That(reader.Available, Is.Zero);
    }

    // Mode 7 (:7128): int grabber, int n, n x int.
    [Test]
    public void GrabRelease() {
        ByteReader reader = Reader(SkillDamagePacket.GrabRelease(500, [601, 602]), SendOp.SkillDamage);
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(500));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(601));
        Assert.That(reader.ReadInt(), Is.EqualTo(602));
        Assert.That(reader.Available, Is.Zero);
    }

    // Mode 8 (:7149): long tag uid, bool create; only on create int skill, short level, int owner, int caster.
    [TestCase(true)]
    [TestCase(false)]
    public void Tag(bool create) {
        ByteReader reader = Reader(SkillDamagePacket.Target(55, create, 10200001, 2, 600, 700), SendOp.SkillDamage);
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(reader.ReadLong(), Is.EqualTo(55));
        Assert.That(reader.ReadBool(), Is.EqualTo(create));
        if (create) {
            Assert.That(reader.ReadInt(), Is.EqualTo(10200001));
            Assert.That(reader.ReadShort(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(600));
            Assert.That(reader.ReadInt(), Is.EqualTo(700));
        }
        Assert.That(reader.Available, Is.Zero);
    }

    // Buff 0x47 (OnBuff :7591): byte mode, three ints (owner, buff object id, caster); add: the state (int start,
    // int end, int id, short level, int stacks, byte enabled) and the long shield; remove: nothing; update: int
    // flags, the state if &1, the shield if &2.
    // Built as DisabledBuffTests.Held does: the constructor checks the effect's begin condition, which needs a field.
    private static Buff HeldBuff(FieldPlayer caster, FieldPlayer owner) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var buff = (Buff) RuntimeHelpers.GetUninitializedObject(typeof(Buff));
        void Set(string field, object value) => typeof(Buff).GetField(field, any)!.SetValue(buff, value);
        Set(nameof(Buff.Metadata), DisabledBuffTests.Effect(10200221));
        Set(nameof(Buff.ObjectId), 4321);
        Set("<Caster>k__BackingField", caster);
        Set("<Owner>k__BackingField", owner);
        Set("<StartTick>k__BackingField", 1_000L);
        Set("<EndTick>k__BackingField", 9_000L);
        Set("<Stacks>k__BackingField", 1);
        Set("<Enabled>k__BackingField", true);
        buff.ShieldHealth = 5000;
        return buff;
    }

    private static void BuffHead(ByteReader reader, byte mode, Buff buff) {
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        Assert.That(reader.ReadInt(), Is.EqualTo(buff.Owner.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(4321));
        Assert.That(reader.ReadInt(), Is.EqualTo(buff.Caster.ObjectId));
    }

    private static void BuffState(ByteReader reader, Buff buff) {
        Assert.That(reader.ReadInt(), Is.EqualTo(1_000));
        Assert.That(reader.ReadInt(), Is.EqualTo(9_000));
        Assert.That(reader.ReadInt(), Is.EqualTo(10200221));
        Assert.That(reader.ReadShort(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.True);
    }

    [Test]
    public void BuffAddAndRemove() {
        Buff buff = HeldBuff(Player(), Player());
        ByteReader add = Reader(BuffPacket.Add(buff), SendOp.Buff);
        BuffHead(add, 0, buff);
        BuffState(add, buff);
        Assert.That(add.ReadLong(), Is.EqualTo(5000));
        Assert.That(add.Available, Is.Zero);

        ByteReader remove = Reader(BuffPacket.Remove(buff), SendOp.Buff);
        BuffHead(remove, 1, buff);
        Assert.That(remove.Available, Is.Zero);
    }

    [TestCase(BuffFlag.UpdateBuff)]
    [TestCase(BuffFlag.UpdateShield)]
    [TestCase(BuffFlag.UpdateBuff | BuffFlag.UpdateShield)]
    public void BuffUpdate(BuffFlag flag) {
        Buff buff = HeldBuff(Player(), Player());
        ByteReader reader = Reader(BuffPacket.Update(buff, flag), SendOp.Buff);
        BuffHead(reader, 2, buff);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) flag));
        if (flag.HasFlag(BuffFlag.UpdateBuff)) {
            BuffState(reader, buff);
        }
        if (flag.HasFlag(BuffFlag.UpdateShield)) {
            Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        }
        Assert.That(reader.Available, Is.Zero);
    }

    // SkillUse 0x3C (OnSkillUse :3297): long uid, int tick, int caster, int skill, short level, byte motion, short
    // point, float direction, float rotation, short speed x 10, byte effect; then (feature 0xae) bool hold, and on
    // hold int and a unicode string.
    [TestCase(false)]
    [TestCase(true)]
    public void SkillUse(bool hold) {
        FieldPlayer caster = Player();
        SkillRecord cast = Cast(caster);
        cast.IsHold = hold;
        cast.HoldInt = 9;
        cast.HoldString = "Hold_A";
        // The use's direction, not the attack's: an observer moves a key-moved cast along it.
        cast.CastDirection = new Vector3(0.6f, -0.8f, 0);

        ByteReader reader = Reader(SkillPacket.Use(cast), SendOp.SkillUse);
        Assert.That(reader.ReadLong(), Is.EqualTo(9_000_000_001));
        Assert.That(reader.ReadInt(), Is.EqualTo(123_456));
        Assert.That(reader.ReadInt(), Is.EqualTo(caster.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(10200001));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That((reader.ReadShort(), reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 100, (short) -200, (short) 300)));
        Assert.That((reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((0.6f, -0.8f, 0f)));
        Assert.That((reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((0f, 0f, 90f)));
        Assert.That(reader.ReadShort(), Is.EqualTo(15));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadBool(), Is.EqualTo(hold));
        if (hold) {
            Assert.That(reader.ReadInt(), Is.EqualTo(9));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Hold_A"));
        }
        Assert.That(reader.Available, Is.Zero);
    }

    // SkillCooldown 0x42 (OnSkillCooldown :7856): byte n; per entry a long (skill id low, group high), int end tick,
    // int recharge uses: 16 bytes.
    [Test]
    public void SkillCooldown() {
        var first = new SkillCooldown(10200001, 1) { GroupId = 7, EndTick = 0x1_0000_1000, Charges = 2 };
        var second = new SkillCooldown(10200002, 1) { EndTick = 0 };
        ByteWriter writer = SkillPacket.Cooldown(first, second);
        ByteReader reader = Reader(writer, SendOp.SkillCooldown);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo((7L << 32) | 10200001));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x1000));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(10200002));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 2 * 16));
    }

    // UserSync (OnUserSync :2461, commonfiles/statesyncobject.cpp:150): int object id, byte n; per entry byte state,
    // byte sub state, short point, short, byte animation (two floats if &0x80), short speed, byte, short, short,
    // byte flags with each flag's fields, int sync number. A subclass's fields follow the base.
    private static StateSync Sync(StateSync sync, byte animation, StateSync.Flag flags) {
        sync.State = (ActorState) 1;
        sync.SubState = (ActorSubState) 0;
        sync.Position = new Vector3(1, 2, 3);
        sync.Rotation = 450;
        sync.Animation = animation;
        sync.UnknownFloat1 = 0.5f;
        sync.UnknownFloat2 = 1.5f;
        sync.Speed = new Vector3(4, 5, 6);
        sync.Unknown1 = 2;
        sync.Rotation2 = 900;
        sync.Unknown3 = 3;
        sync.Flags = flags;
        sync.Flag1Unknown = 11;
        sync.Flag2Unknown = 12;
        sync.Flag3Unknown = 13;
        sync.AnimationName = "Idle_A";
        sync.Flag5Unknown1 = new Vector3(7, 8, 9);
        sync.Flag5Unknown2 = "a";
        sync.Flag6Unknown1 = 16;
        sync.Flag6Unknown2 = "b";
        sync.SyncNumber = 99;
        return sync;
    }

    private static void SyncBase(ByteReader reader, byte animation, StateSync.Flag flags) {
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That((reader.ReadShort(), reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 1, (short) 2, (short) 3)));
        Assert.That(reader.ReadShort(), Is.EqualTo(450));
        Assert.That(reader.ReadByte(), Is.EqualTo(animation));
        if ((animation & 0x80) != 0) {
            Assert.That((reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((0.5f, 1.5f)));
        }
        Assert.That((reader.ReadShort(), reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 4, (short) 5, (short) 6)));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadShort(), Is.EqualTo(900));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) flags));
        if (flags.HasFlag(StateSync.Flag.Flag1)) Assert.That(reader.ReadInt(), Is.EqualTo(11));
        if (flags.HasFlag(StateSync.Flag.Flag2)) Assert.That(reader.ReadInt(), Is.EqualTo(12));
        if (flags.HasFlag(StateSync.Flag.Flag3)) Assert.That(reader.ReadShort(), Is.EqualTo(13));
        if (flags.HasFlag(StateSync.Flag.Animation)) Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Idle_A"));
        if (flags.HasFlag(StateSync.Flag.Flag5)) {
            Assert.That((reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((7f, 8f, 9f)));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("a"));
        }
        if (flags.HasFlag(StateSync.Flag.Flag6)) {
            Assert.That(reader.ReadInt(), Is.EqualTo(16));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(99));
    }

    [TestCase((byte) 0, StateSync.Flag.None)]
    [TestCase((byte) 0x81, StateSync.Flag.None)]
    [TestCase((byte) 0, (StateSync.Flag) 0x3f)]
    public void UserSync(byte animation, StateSync.Flag flags) {
        using var buffer = new PoolByteWriter();
        buffer.Player(500, Sync(new StateSync(), animation, flags), Sync(new StateSync(), 0, StateSync.Flag.None));
        var reader = new ByteReader(buffer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.UserSync));
        Assert.That(reader.ReadInt(), Is.EqualTo(500));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        SyncBase(reader, animation, flags);
        SyncBase(reader, 0, StateSync.Flag.None);
        Assert.That(reader.Available, Is.Zero);
    }

    // State 0x3a's object (CoupleEmotion): the base, then int, int, byte, six floats.
    [Test]
    public void UserSyncCoupleEmotion() {
        var sync = (StateSyncCoupleEmotion) Sync(new StateSyncCoupleEmotion {
            UnknownCoupleEmotionInt1 = 21,
            UnknownCoupleEmotionInt2 = 22,
            UnknownCoupleEmotionByte = 23,
            UnknownCoupleEmotionVector1 = new Vector3(1, 2, 3),
            UnknownCoupleEmotionVector2 = new Vector3(4, 5, 6),
        }, 0, StateSync.Flag.None);
        using var buffer = new PoolByteWriter();
        buffer.Player(500, sync);
        var reader = new ByteReader(buffer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.UserSync));
        Assert.That(reader.ReadInt(), Is.EqualTo(500));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        SyncBase(reader, 0, StateSync.Flag.None);
        Assert.That(reader.ReadInt(), Is.EqualTo(21));
        Assert.That(reader.ReadInt(), Is.EqualTo(22));
        Assert.That(reader.ReadByte(), Is.EqualTo(23));
        for (int i = 1; i <= 6; i++) {
            Assert.That(reader.ReadFloat(), Is.EqualTo((float) i));
        }
        Assert.That(reader.Available, Is.Zero);
    }

    // NpcControl's jump block (CNpc::ApplySyncBlock, sequence -2 / -3): byte absolute, then two points and two
    // floats, or one point.
    [TestCase(true)]
    [TestCase(false)]
    public void NpcJumpBlock(bool absolute) {
        using var buffer = new PoolByteWriter();
        buffer.WriteJump(absolute, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 0.75f, 1.25f);
        var reader = new ByteReader(buffer.ToArray(), 0);
        Assert.That(reader.ReadBool(), Is.EqualTo(absolute));
        if (absolute) {
            Assert.That((reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((1f, 2f, 3f)));
        }
        Assert.That((reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo((4f, 5f, 6f)));
        if (absolute) {
            Assert.That(reader.ReadFloat(), Is.EqualTo(0.75f));
            Assert.That(reader.ReadFloat(), Is.EqualTo(1.25f));
        }
        Assert.That(reader.Available, Is.Zero);
    }
}
