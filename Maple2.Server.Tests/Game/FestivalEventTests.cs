using System;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game;

// GameEventFestival: the client keeps each running festival's value1 and value2 (FestivalContext) and lets a
// FestivalEvent item whose itemUseType is not 0 be used only while a festival of that type runs.
public class FestivalEventTests {
    // Before, the table's Festival rows (KR 831001-831004) were dropped at ingest and never sent, so the client
    // refused every FestivalEvent item with "the event has not started".
    [Test]
    public void GameEvent_FestivalIsLaidOutForTheClient() {
        var metadata = new GameEventMetadata(831002, GameEventType.Festival, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1000),
            TimeSpan.Zero, TimeSpan.Zero, [], new Festival(1, 1), "1", "1", "", "");
        var gameEvent = new GameEvent(metadata);
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Festival"));
        Assert.That(reader.ReadInt(), Is.EqualTo(831002)); // GameEvent::vfunc_4
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // FestivalContext::Read m_18
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // m_1c, the use type
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}
