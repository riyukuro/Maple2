using System;
using System.Collections.Generic;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// FieldProperty against WorldService::FUN_141bca6d0 (worldservice_1.cpp:924) and the FieldProperty readers
// (commonfiles/fieldproperty.cpp); TimeScale, UserSkinColor, UserBattle, Taxi, FindFields, DeadUser,
// DungeonReviveCount, LocalCamera, PlayNpcSound and PlaySystemSound against their CSceneStateGame handlers;
// CameraInterpolation against CTriggerService::OnCameraInterpolation (triggerservice.cpp:508); UserMaid and FieldMaid
// against CUserMaidManager and CFieldMaidManager::OnPacket (maidmanager.cpp) and CMaid::ReadFromPacket
// (commonfiles/maid.cpp); DynamicChannel against CNetworkService::OnDynamicChannel (networkservice.cpp:692).
public class FieldMiscLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // 0: u32 n, n x (u8 type, the type's fields); 1: u8 type, fields; 2: u8 type. Every server type is 1-11, the
    // types the client's factory creates, so the list never stops early.
    [Test]
    public void FieldProperty() {
        var load = new ByteWriter();
        load.WriteByte(0);
        load.WriteInt(11);
        load.WriteByte(1);
        load.WriteFloat(2.5f);
        load.WriteByte(2);
        load.WriteLong(0x0102030405060708);
        load.WriteInt(0x11);
        load.WriteByte(3);
        load.WriteByte(4);
        load.WriteByte(5);
        load.WriteUnicodeString("a");
        load.WriteUnicodeString("b");
        load.WriteByte(6);
        load.WriteFloat(450);
        load.WriteFloat(1);
        load.WriteFloat(2);
        load.WriteFloat(3);
        load.WriteBool(true);
        load.WriteByte(0x12);
        load.WriteBool(true);
        load.WriteByte(7);
        load.WriteByte((byte) WeatherType.Rain);
        load.WriteByte(8);
        load.WriteBytes(new byte[] { 1, 2, 3 });
        load.WriteBool(true);
        load.WriteFloat(0.5f);
        load.WriteByte(9);
        load.WriteBytes(new byte[] { 4, 5, 6, 7, 8, 9 });
        load.WriteBool(false);
        load.WriteFloat(0.25f);
        load.WriteByte(10);
        load.WriteBool(true);
        load.WriteByte(11);
        load.WriteBool(true);

        var properties = new List<IFieldProperty> {
            new FieldPropertyGravity(2.5f),
            new FieldPropertyMusicConcert { CharacterId = 0x0102030405060708, PerformanceEndTick = 0x11 },
            new FieldPropertyHidePlayer(),
            new FieldPropertyLockPlayer(),
            new FieldPropertyUserTagSymbol { Symbol1 = "a", Symbol2 = "b" },
            new FieldPropertySightRange { Fades = [1, 2, 3], Unknown = true, Opacity = 0x12, Opaque = true },
            new FieldPropertyWeather { WeatherType = WeatherType.Rain },
            new FieldPropertyAmbientLight { Color = new Byte3(1, 2, 3), Restore = true, Value = 0.5f },
            new FieldPropertyDirectionalLight {
                DiffuseColor = new Byte3(4, 5, 6), SpecularColor = new Byte3(7, 8, 9), Restore = false, Value = 0.25f,
            },
            new FieldPropertyLocalCamera { Enabled = true },
            new FieldPropertyPhotoStudio { Enabled = true },
        };
        Assert.That(Body(FieldPropertyPacket.Load(properties)), Is.EqualTo(load.ToArray()));

        var add = new ByteWriter();
        add.WriteByte(1);
        add.WriteByte(7);
        add.WriteByte((byte) WeatherType.Snow);
        Assert.That(Body(FieldPropertyPacket.Add(new FieldPropertyWeather { WeatherType = WeatherType.Snow })), Is.EqualTo(add.ToArray()));
        Assert.That(Body(FieldPropertyPacket.Remove(Maple2.Model.Enum.FieldProperty.Weather)), Is.EqualTo(new byte[] { 2, 7 }));
    }

    // bool, float, float, float, u8.
    [Test]
    public void TimeScale() {
        var expected = new ByteWriter();
        expected.WriteBool(true);
        expected.WriteFloat(1);
        expected.WriteFloat(0.5f);
        expected.WriteFloat(2);
        expected.WriteByte(3);
        Assert.That(Body(FieldPropertyPacket.TimeScale(true, 1, 0.5f, 2, 3)), Is.EqualTo(expected.ToArray()));
    }

    // UserSkinColor: i32 object, i32 colour, i32 colour. UserBattle: i32 object, u8. DeadUser: i32 object, u8.
    [Test]
    public void Users() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var player = test.Session.Player;

        var skin = new ByteWriter();
        skin.WriteInt(player.ObjectId);
        skin.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.That(Body(UserSkinColorPacket.Update(player, new SkinColor(new Color(1, 2, 3, 4), new Color(5, 6, 7, 8)))),
            Is.EqualTo(skin.ToArray()));

        var battle = new ByteWriter();
        battle.WriteInt(player.ObjectId);
        battle.WriteBool(false);
        Assert.That(Body(SkillPacket.InBattle(player)), Is.EqualTo(battle.ToArray()));

        var dead = new ByteWriter();
        dead.WriteInt(0x21);
        dead.WriteBool(true);
        Assert.That(Body(DeadUserPacket.Dead(0x21, true)), Is.EqualTo(dead.ToArray()));
    }

    // Taxi: i32 n, n x u32, bool. FindFields: i32 n, n x u32. DungeonReviveCount: i32.
    [Test]
    public void TaxiAndFields() {
        var taxi = new ByteWriter();
        taxi.WriteInt(2);
        taxi.WriteInt(0x31);
        taxi.WriteInt(0x32);
        taxi.WriteBool(true);
        Assert.That(Body(TaxiPacket.RevealTaxi(0x31, 0x32)), Is.EqualTo(taxi.ToArray()));

        var fields = new ByteWriter();
        fields.WriteInt(1);
        fields.WriteInt(0x33);
        Assert.That(Body(FindFieldsPacket.Add(new[] { 0x33 })), Is.EqualTo(fields.ToArray()));

        var revive = new ByteWriter();
        revive.WriteInt(2);
        Assert.That(Body(RevivalPacket.DungeonReviveCount(2)), Is.EqualTo(revive.ToArray()));
    }

    // 0: eight u16; 1: u16 n, n x u16.
    [Test]
    public void DynamicChannel() {
        var load = new ByteWriter();
        load.WriteByte(0);
        foreach (short value in new short[] { 10, 100, 100, 100, 100, 10, 10, 10 }) {
            load.WriteShort(value);
        }
        Assert.That(Body(ChannelPacket.Load(new List<int>())), Is.EqualTo(load.ToArray()));

        var update = new ByteWriter();
        update.WriteByte(1);
        update.WriteShort(2);
        update.WriteShort(1);
        update.WriteShort(3);
        Assert.That(Body(ChannelPacket.Update(new List<short> { 1, 3 })), Is.EqualTo(update.ToArray()));
    }

    // LocalCamera: i32, bool, i32. CameraInterpolation: float. PlayNpcSound: i32 npc, unicode. PlaySystemSound: unicode.
    [Test]
    public void CameraAndSound() {
        var local = new ByteWriter();
        local.WriteInt(0x41);
        local.WriteBool(true);
        local.WriteInt(0x42);
        Assert.That(Body(CameraPacket.Local(0x41, true, 0x42)), Is.EqualTo(local.ToArray()));

        var interpolate = new ByteWriter();
        interpolate.WriteFloat(1.5f);
        Assert.That(Body(CameraPacket.Interpolate(1.5f)), Is.EqualTo(interpolate.ToArray()));

        var npc = new ByteWriter();
        npc.WriteInt(0x43);
        npc.WriteUnicodeString("voice");
        Assert.That(Body(SoundPacket.Npc(0x43, "voice")), Is.EqualTo(npc.ToArray()));

        var system = new ByteWriter();
        system.WriteUnicodeString("sound");
        Assert.That(Body(SoundPacket.System("sound")), Is.EqualTo(system.ToArray()));
    }

    // CMaid::ReadFromPacket after the manager's mode byte: u8 mode, i64, i64 uid, i64, i64, i64, i32 item, i32 maid,
    // u8, i32 mood, i32 level, i32 exp, i64 expiry, i32 n, n x i64, then MaidProfile::Read's 13 unicode strings.
    private static void MaidBlock(ByteWriter writer) {
        writer.WriteByte(0);
        writer.WriteLong(0);
        writer.WriteLong(0x0102030405060708);
        writer.WriteLong(0x51);
        writer.WriteLong(0x51);
        writer.WriteLong(0);
        writer.WriteInt(0x52);
        writer.WriteInt(0x53);
        writer.WriteByte(1);
        writer.WriteInt(0x54);
        writer.WriteInt(0x55);
        writer.WriteInt(0x56);
        writer.WriteLong(0x57);
        writer.WriteInt(0);
        writer.WriteUnicodeString("title");
        writer.WriteUnicodeString("name");
        for (int i = 0; i < 9; i++) {
            writer.WriteUnicodeString("");
        }
        writer.WriteUnicodeString("owner");
        writer.WriteUnicodeString("");
    }

    private static Maid NewMaid() => new() {
        Uid = 0x0102030405060708,
        OwnerId = 0x61,
        ItemId = 0x52,
        MaidId = 0x53,
        Mood = 0x54,
        Level = 0x55,
        Exp = 0x56,
        CreationTime = 0x51,
        ExpiryTime = 0x57,
        Profile = new MaidProfile { Title = "title", Name = "name", OwnerName = "owner" },
    };

    // UserMaid 0: i32 n, n x maid; 1 and 3: one maid; 2: i64 uid; 4: i32 npc object.
    [Test]
    public void UserMaid() {
        var list = new ByteWriter();
        list.WriteByte(0);
        list.WriteInt(1);
        MaidBlock(list);
        Assert.That(Body(UserMaidPacket.List(new List<Maid> { NewMaid() })), Is.EqualTo(list.ToArray()));

        foreach ((byte command, ByteWriter packet) in new[] { ((byte) 1, UserMaidPacket.Add(NewMaid())), ((byte) 3, UserMaidPacket.Update(NewMaid())) }) {
            var expected = new ByteWriter();
            expected.WriteByte(command);
            MaidBlock(expected);
            Assert.That(Body(packet), Is.EqualTo(expected.ToArray()));
        }

        var remove = new ByteWriter();
        remove.WriteByte(2);
        remove.WriteLong(0x0102030405060708);
        Assert.That(Body(UserMaidPacket.Remove(0x0102030405060708)), Is.EqualTo(remove.ToArray()));

        var property = new ByteWriter();
        property.WriteByte(4);
        property.WriteInt(0x62);
        Assert.That(Body(UserMaidPacket.Property(0x62)), Is.EqualTo(property.ToArray()));
    }

    // FieldMaid 0: one maid; 1: i64 uid.
    [Test]
    public void FieldMaid() {
        var add = new ByteWriter();
        add.WriteByte(0);
        MaidBlock(add);
        Assert.That(Body(FieldMaidPacket.Add(NewMaid())), Is.EqualTo(add.ToArray()));

        var remove = new ByteWriter();
        remove.WriteByte(1);
        remove.WriteLong(0x0102030405060708);
        Assert.That(Body(FieldMaidPacket.Remove(0x0102030405060708)), Is.EqualTo(remove.ToArray()));
    }
}
