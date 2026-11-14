using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: UIWorldChampionRewardDialog::OnPacket reads one mode byte; mode 11 reads nothing more.
public class WorldChampionPacketTests {
    [Test]
    public void NoSeasonIsOneByte() {
        ByteWriter writer = WorldChampionPacket.NoSeason();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.WorldChampion));
            Assert.That(reader.ReadByte(), Is.EqualTo(11));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
            Assert.That(writer.Length, Is.EqualTo(2 + 1));
        });
    }

    private static readonly WorldChampionTable.Season Season1 = new(1, 2000423, 100,
        new DateTime(2026, 11, 13, 0, 0, 0), new DateTime(2026, 11, 15, 23, 59, 59), 1500000, 180510006, [
            new WorldChampionTable.Step(1, 23500110, 20000001),
            new WorldChampionTable.Step(2, 23500111, 20000002),
            new WorldChampionTable.Step(3, 23500112, 20000003),
        ]);

    // Mode 8 (uiworldchampionrewarddialog.cpp:253-267): uint season, CMsTime begin and end (8 bytes, unix seconds),
    // byte step count, byte step, uint field, then a vector of uint (int count) the client asserts is the step count.
    [Test]
    public void SeasonLayout() {
        ByteWriter writer = WorldChampionPacket.Season(Season1, 2);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.WorldChampion));
            Assert.That(reader.ReadByte(), Is.EqualTo(8));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(new DateTimeOffset(Season1.Begin).ToUnixTimeSeconds()));
            Assert.That(reader.ReadLong(), Is.EqualTo(new DateTimeOffset(Season1.End).ToUnixTimeSeconds()));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.ReadByte(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(2000423));
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000003));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // A cleared season sends the step past the count (the client shows s_world_champion_clear_title).
    [Test]
    public void SeasonCleared() {
        ByteWriter writer = WorldChampionPacket.Season(Season1, 4);
        Assert.That(writer.Buffer[2 + 1 + 4 + 8 + 8 + 1], Is.EqualTo(4));
    }

    // Mode 10 (:270-274): the next season's begin, 8 bytes.
    [Test]
    public void NextSeasonLayout() {
        ByteWriter writer = WorldChampionPacket.NextSeason(Season1.Begin);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.WorldChampion));
            Assert.That(reader.ReadByte(), Is.EqualTo(10));
            Assert.That(reader.ReadLong(), Is.EqualTo(new DateTimeOffset(Season1.Begin).ToUnixTimeSeconds()));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // The modes the client writes and the opcodes it uses.
    [Test]
    public void Modes() {
        Assert.Multiple(() => {
            Assert.That(WorldChampionHandler.OpenMode, Is.EqualTo(8));
            Assert.That(WorldChampionHandler.MoveMode, Is.EqualTo(9));
            Assert.That((int) RecvOp.WorldChampion, Is.EqualTo(0x00AF));
            Assert.That((int) SendOp.WorldChampion, Is.EqualTo(0x011E));
            Assert.That(new WorldChampionHandler().OpCode, Is.EqualTo(RecvOp.WorldChampion));
        });
    }
}
