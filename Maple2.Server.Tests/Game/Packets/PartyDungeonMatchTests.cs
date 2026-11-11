using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game.Packets;

// Party 32 and 33: the dungeon match requests are read as the client writes them.
public class PartyDungeonMatchTests {
    private static ByteReader Reader(Action<ByteWriter> write) {
        var writer = new ByteWriter();
        write(writer);
        return new ByteReader(writer.ToArray(), 0);
    }

    [Test]
    public void SearchBody() {
        PartyDungeonMatchRequest? request = PartyDungeonMatch.ReadSearch(Reader(writer => {
            writer.WriteByte(1);
            writer.WriteInt(2);
            writer.WriteInt(23001);
            writer.WriteInt(23002);
            writer.WriteBool(true);
        }));
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.Value.Kind, Is.EqualTo(1));
        Assert.That(request.Value.RoomIds, Is.EqualTo(new[] { 23001, 23002 }));
        Assert.That(request.Value.Rewarded, Is.True);
    }

    [Test]
    public void SearchWithoutRooms() {
        // The control center sends its bookmarks, which may be none, and false.
        PartyDungeonMatchRequest? request = PartyDungeonMatch.ReadSearch(Reader(writer => {
            writer.WriteByte(1);
            writer.WriteInt(0);
            writer.WriteBool(false);
        }));
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.Value.RoomIds, Is.Empty);
        Assert.That(request.Value.Rewarded, Is.False);
    }

    [Test]
    public void SearchCountOutsideTheBody() {
        Assert.That(PartyDungeonMatch.ReadSearch(Reader(writer => {
            writer.WriteByte(1);
            writer.WriteInt(int.MaxValue);
            writer.WriteInt(23001);
            writer.WriteBool(true);
        })), Is.Null);
        Assert.That(PartyDungeonMatch.ReadSearch(Reader(writer => {
            writer.WriteByte(1);
            writer.WriteInt(-1);
            writer.WriteBool(true);
        })), Is.Null);
        // The bool after the ids is missing.
        Assert.That(PartyDungeonMatch.ReadSearch(Reader(writer => {
            writer.WriteByte(1);
            writer.WriteInt(1);
            writer.WriteInt(23001);
        })), Is.Null);
        Assert.That(PartyDungeonMatch.ReadSearch(Reader(writer => writer.WriteByte(1))), Is.Null);
    }

    [Test]
    public void PartySearchBody() {
        Assert.That(PartyDungeonMatch.ReadPartySearch(Reader(writer => writer.WriteInt(23001))), Is.EqualTo(23001));
        Assert.That(PartyDungeonMatch.ReadPartySearch(Reader(writer => writer.WriteByte(1))), Is.Null);
    }
}
