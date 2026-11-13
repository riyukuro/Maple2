using System;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// ProxyGameObj 0x7E against the client's reader, CSceneStateGame::FUN_14172f500 (scenestategame.cpp:11335).
public class ProxyGameObjLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // Mode 3: int id, long, long, unicode name, unicode picture, unicode motto, byte kind (dead), Vector3, short level,
    // short job code, int job, int x 3 (the home plot map, plot and apartment), unicode home name, int gear score,
    // short state (0x3f = observer), then three ints (Unk_IntTriple::ReadFrom: the trophy counts).
    [Test]
    public void AddPlayer() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Character character = player.Value.Character;
        character.Motto = "motto";
        character.AchievementInfo = new AchievementInfo { Combat = 1, Adventure = 2, Lifestyle = 3 };

        var expected = new ByteWriter();
        expected.WriteByte(3);
        expected.WriteInt(player.ObjectId);
        expected.WriteLong(character.Id);
        expected.WriteLong(player.Value.Account.Id);
        expected.WriteUnicodeString(character.Name);
        expected.WriteUnicodeString(character.Picture);
        expected.WriteUnicodeString("motto");
        expected.WriteByte(0);
        expected.WriteFloat(player.Position.X);
        expected.WriteFloat(player.Position.Y);
        expected.WriteFloat(player.Position.Z);
        expected.WriteShort(character.Level);
        expected.WriteShort((short) character.Job.Code());
        expected.WriteInt((int) character.Job);
        expected.WriteInt(player.Value.Home.PlotMapId);
        expected.WriteInt(player.Value.Home.PlotNumber);
        expected.WriteInt(player.Value.Home.ApartmentNumber);
        expected.WriteUnicodeString(player.Value.Home.Indoor.Name);
        expected.WriteInt(player.Stats.Values.GearScore);
        expected.WriteShort((short) player.State);
        expected.WriteInt(1);
        expected.WriteInt(2);
        expected.WriteInt(3);
        Assert.That(Body(ProxyObjectPacket.AddPlayer(player)), Is.EqualTo(expected.ToArray()));
    }

    // Mode 5: int id, byte flags; 1 byte kind, 2 Vector3, 4 short level, 8 short job code + int job, 0x10 unicode
    // motto, 0x20 int gear score, 0x40 short state, in this order.
    [Test]
    public void UpdatePlayer_AllFlags() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Character character = player.Value.Character;
        character.Motto = "motto";

        var expected = new ByteWriter();
        expected.WriteByte(5);
        expected.WriteInt(player.ObjectId);
        expected.WriteByte(0x7F);
        expected.WriteByte(0);
        expected.WriteFloat(player.Position.X);
        expected.WriteFloat(player.Position.Y);
        expected.WriteFloat(player.Position.Z);
        expected.WriteShort(character.Level);
        expected.WriteShort((short) character.Job.Code());
        expected.WriteInt((int) character.Job);
        expected.WriteUnicodeString("motto");
        expected.WriteInt(player.Stats.Values.GearScore);
        expected.WriteShort((short) player.State);
        Assert.That(Body(ProxyObjectPacket.UpdatePlayer(player, PlayerObjectFlag.All)), Is.EqualTo(expected.ToArray()));
        Assert.That((byte) PlayerObjectFlag.All, Is.EqualTo(0x7F));
    }

    // Mode 5 with one flag reads only that field.
    [Test]
    public void UpdatePlayer_JobOnly() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;

        var expected = new ByteWriter();
        expected.WriteByte(5);
        expected.WriteInt(player.ObjectId);
        expected.WriteByte(8);
        expected.WriteShort((short) player.Value.Character.Job.Code());
        expected.WriteInt((int) player.Value.Character.Job);
        Assert.That(Body(ProxyObjectPacket.UpdatePlayer(player, PlayerObjectFlag.Job)), Is.EqualTo(expected.ToArray()));
    }
}
