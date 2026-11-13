using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// FieldPortal 0x48 against CSceneStateGame::OnFieldPortal (scenestategame.cpp:7227), UserMoveByPortal against
// OnUserMoveByPortal (:5775), RequestFieldEnter against OnRequestFieldEnter (:2335) and FieldEntrance against
// OnFieldEntrance (:2366).
public class FieldPortalLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static void Vector(ByteWriter writer, Vector3 value) {
        writer.WriteFloat(value.X);
        writer.WriteFloat(value.Y);
        writer.WriteFloat(value.Z);
    }

    private static (TestGameSession, FieldPortal) Portal() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var value = new Portal(0x11, 0x02030405, 0, PortalType.DungeonEnter, PortalActionType.Touch, new Vector3(1, 2, 3),
            new Vector3(0, 0, 90), new Vector3(4, 5, 6), 0, 0, true, true, true);
        var portal = new FieldPortal(test.Session.Field!, 0x0A0B0C0D, value) {
            Position = new Vector3(1, 2, 3),
            Rotation = new Vector3(0, 0, 90),
            Model = "model",
            HomeId = 0x0102030405060708,
            EndTick = 0x21,
            StartTick = 0x22,
            OwnerName = "owner",
            Password = "pw",
            MinimapVisible = false,
        };
        return (test, portal);
    }

    // Mode 0: int id, byte visible, byte enabled, Vector3 pos, Vector3 rotation (prop 0x68c7c29d), Vector3 size
    // (0xfb190620), unicode model, int target map (0xa162d321), int object id, int kind (0x1b8b1161, 1 = touch),
    // byte minimap (0xe084c8de), long, byte type, int, short, int, byte password, unicode x 3.
    [Test]
    public void Add() {
        (_, FieldPortal portal) = Portal();
        var expected = new ByteWriter();
        expected.WriteByte(0);
        expected.WriteInt(0x11);
        expected.WriteBool(true);
        expected.WriteBool(true);
        Vector(expected, portal.Position);
        Vector(expected, portal.Rotation);
        Vector(expected, new Vector3(4, 5, 6));
        expected.WriteUnicodeString("model");
        expected.WriteInt(0x02030405);
        expected.WriteInt(0x0A0B0C0D);
        expected.WriteInt(1);
        expected.WriteBool(false);
        expected.WriteLong(0x0102030405060708);
        expected.WriteByte((byte) PortalType.DungeonEnter);
        expected.WriteInt(0x21);
        expected.WriteShort(0);
        expected.WriteInt(0x22);
        expected.WriteBool(true);
        expected.WriteUnicodeString("owner");
        expected.WriteUnicodeString("");
        expected.WriteUnicodeString("");
        Assert.That(Body(PortalPacket.Add(portal)), Is.EqualTo(expected.ToArray()));
    }

    // Mode 1: int id. Mode 2: int id, byte visible, byte enabled, byte minimap, short. Mode 3: int id, two Vector3.
    [Test]
    public void RemoveUpdateMove() {
        (_, FieldPortal portal) = Portal();
        portal.Enabled = false;

        var remove = new ByteWriter();
        remove.WriteByte(1);
        remove.WriteInt(0x11);
        Assert.That(Body(PortalPacket.Remove(0x11)), Is.EqualTo(remove.ToArray()));

        var update = new ByteWriter();
        update.WriteByte(2);
        update.WriteInt(0x11);
        update.WriteBool(true);
        update.WriteBool(false);
        update.WriteBool(false);
        update.WriteShort(7);
        Assert.That(Body(PortalPacket.Update(portal, 7)), Is.EqualTo(update.ToArray()));

        var move = new ByteWriter();
        move.WriteByte(3);
        move.WriteInt(0x11);
        Vector(move, portal.Position);
        Vector(move, portal.Rotation);
        Assert.That(Body(PortalPacket.Move(portal)), Is.EqualTo(move.ToArray()));
    }

    // UserMoveByPortal: int object id, Vector3 pos, Vector3 rot, byte flag. InvincibleEffect: no body.
    [Test]
    public void MoveByPortal() {
        (TestGameSession test, FieldPortal portal) = Portal();
        var expected = new ByteWriter();
        expected.WriteInt(test.Session.Player.ObjectId);
        Vector(expected, new Vector3(1, 2, 28));
        Vector(expected, portal.Value.Rotation);
        expected.WriteBool(true);
        Assert.That(Body(PortalPacket.MoveByPortal(test.Session.Player, portal.Value)), Is.EqualTo(expected.ToArray()));
        Assert.That(PortalPacket.InvincibleEffect().Length, Is.EqualTo(2));
    }

    // RequestFieldEnter: byte error; 0: int map, byte, byte, int, int, Vector3 pos, Vector3 rot, int key (echoed).
    [Test]
    public void RequestFieldEnter() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var player = test.Session.Player;
        // The test field has no instance metadata: a solo instance with an id of its own.
        typeof(FieldManager).GetProperty(nameof(FieldManager.FieldInstance))!.SetValue(test.Session.Field,
            new InstanceFieldMetadata(2000023, InstanceType.solo, 0x31, false, 0, true, 0, 0, 0, 0));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(player, test.Session.Field);
        var expected = new ByteWriter();
        expected.WriteByte((byte) MigrationError.ok);
        expected.WriteInt(player.Value.Character.MapId);
        expected.WriteByte((byte) player.Field.FieldType);
        expected.WriteByte((byte) player.Field.FieldInstance.Type);
        expected.WriteInt(player.Field.FieldInstance.InstanceId);
        expected.WriteInt(player.Field.DungeonId);
        Vector(expected, player.Position);
        Vector(expected, player.Rotation);
        expected.WriteInt(GameSession.FIELD_KEY);
        Assert.That(Body(FieldEnterPacket.Request(player)), Is.EqualTo(expected.ToArray()));
    }

    // FieldEntrance: 0 = int n, n x (int id, byte limit); 2 = byte limit, int id.
    [Test]
    public void FieldEntrance() {
        var load = new ByteWriter();
        load.WriteByte(0);
        load.WriteInt(1);
        load.WriteInt(0x01020304);
        load.WriteByte((byte) DungeonEnterLimit.Gearscore);
        Assert.That(Body(FieldEntrancePacket.Load(new Dictionary<int, DungeonEnterLimit> { [0x01020304] = DungeonEnterLimit.Gearscore })),
            Is.EqualTo(load.ToArray()));

        var error = new ByteWriter();
        error.WriteByte(2);
        error.WriteByte((byte) DungeonEnterLimit.Vip);
        error.WriteInt(0x01020304);
        Assert.That(Body(FieldEntrancePacket.Error(0x01020304, DungeonEnterLimit.Vip)), Is.EqualTo(error.ToArray()));
    }
}
