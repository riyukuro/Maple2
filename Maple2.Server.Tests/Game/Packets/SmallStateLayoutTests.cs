using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Packets;

// The small scene-state packets against their CSceneStateGame readers (client/ms2/scenestategame.cpp):
// AdventureProtect (FUN_14173f7c0, :5371): u8 state. FollowNpc (OnFollowNpc, :5862): int npc object id.
// StateFallDamage (OnStateFallDamage, :5815): int object id, u32 amount. LevelUp (OnLevelUp, :9511): int object id,
// u16 level. GuideRecord (OnGuideRecord :3876, CGuideManager::ReceiveStates, guideentity_1b.cpp:231): int count x
// (int key, int step), u8 bit set. BirthdayCard (OnBirthdayCard, :6683): u8 mode; 1: int object id, long birthday.
// FieldWar (OnFieldWar, :9102): u8 mode; 0: int war id, long uid. AttributePoint (OnAttributePoint :4270,
// CStatPointManager::OnPacket, statpointmanager.cpp:33): u8 mode; 0 (LoadExtra): int total, int count x (int source,
// int points); 1 (LoadStats): int total, int count x (u8 stat, int points).
public class SmallStateLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void AdventureProtect() {
        ByteReader reader = Read(AdventureProtectPacket.State(2), SendOp.AdventureProtect);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void FollowNpc() {
        ByteReader reader = Read(FollowNpcPacket.FollowNpc(4242), SendOp.FollowNpc);
        Assert.That(reader.ReadInt(), Is.EqualTo(4242));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void FallDamage() {
        ByteReader reader = Read(FallDamagePacket.FallDamage(77, 1500), SendOp.StateFallDamage);
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(1500));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void LevelUp() {
        GameSession session = new TestGameSession().Start();
        session.Player.Value.Character.Level = 31;
        ByteReader reader = Read(LevelUpPacket.LevelUp(session.Player), SendOp.LevelUp);
        Assert.That(reader.ReadInt(), Is.EqualTo(session.Player.ObjectId));
        Assert.That(reader.ReadShort(), Is.EqualTo(31));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void GuideRecord() {
        ByteReader reader = Read(GuideRecordPacket.Load(new Dictionary<int, int> { [5] = 2, [9] = 3 }), SendOp.GuideRecord);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(9));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void BirthdayCard() {
        ByteReader reader = Read(BirthdayCardPacket.Set(12, 1700000000), SendOp.BirthdayCard);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(12));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void FieldWar() {
        ByteReader reader = Read(FieldWarPacket.Announce(3, 1700000500), SendOp.FieldWar);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000500));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void AttributePoint() {
        var attributes = new StatAttributes(new Dictionary<string, int> { ["StatPointLimit_str"] = 100 });
        attributes.Sources[AttributePointSource.Quest] = 4;
        attributes.Allocation[BasicAttribute.Strength] = 3;

        ByteReader reader = Read(AttributePointPacket.Sources(attributes), SendOp.AttributePoint);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        int count = reader.ReadInt();
        Assert.That(count, Is.EqualTo(System.Enum.GetValues<AttributePointSource>().Length));
        var sources = new Dictionary<int, int>();
        for (int i = 0; i < count; i++) {
            sources[reader.ReadInt()] = reader.ReadInt();
        }
        Assert.That(sources[(int) AttributePointSource.Quest], Is.EqualTo(4));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(AttributePointPacket.Allocation(attributes), SendOp.AttributePoint);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
