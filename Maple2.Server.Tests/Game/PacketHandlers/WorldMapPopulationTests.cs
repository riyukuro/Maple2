using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestWorldmap 1 (the world map's crowd view) is answered with Worldmap 1: byte 3, then per field its id, its
// ranking 1 to 3 and the channel (WorldMapManager::LoadAddtionalInfos). It was always an empty list. Only fields of
// the asked world with a world map tile rank. The field ids are made up.
public class WorldMapPopulationTests {
    private const int Busy = 2000901, Quiet = 2000902, NoTile = 2000903, Karkar = 2000904, Calm = 2000905, Empty = 2000906;

    private static List<(int MapId, int Rank, short Channel)> Request(TestGameSession test, GameSession session, int world) {
        test.TakeSent();
        new WorldMapHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(world);
        }));
        byte[][] sent = test.TakeSent().ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        var reader = new ByteReader(sent[0], 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Worldmap));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        int count = reader.ReadInt();
        var entries = new List<(int, int, short)>();
        for (int i = 0; i < count; i++) {
            entries.Add((reader.ReadInt(), reader.ReadInt(), reader.ReadShort()));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return entries;
    }

    private static TestGameSession Setup() {
        var test = new TestGameSession();
        test.SetTable(new WorldMapTable(new[] { Busy, Quiet, Karkar, Calm, Empty }.Select(id => new WorldMapTable.Map(id, 0, 0, 0, 1)).ToList()));
        MapMetadata karkar = TestGameSession.Map(Karkar);
        test.AddMap(TestGameSession.Map(Busy), TestGameSession.Map(Quiet), TestGameSession.Map(NoTile), TestGameSession.Map(Calm),
            TestGameSession.Map(Empty), karkar with { Property = karkar.Property with { Continent = Continent.KarkarIsland } });
        return test;
    }

    private static MapPopulationResponse.Types.Entry Entry(int mapId, int channel, int count) => new() { MapId = mapId, Channel = channel, Count = count };

    [Test]
    public void TheBusiestFieldsOfTheWorld_AreRanked() {
        TestGameSession test = Setup();
        GameSession session = test.Start();
        test.World.Population = new MapPopulationResponse {
            Entries = {
                Entry(Busy, 1, 4), Entry(Busy, 2, 6), Entry(Quiet, 1, 2), Entry(NoTile, 1, 9), Entry(Karkar, 1, 8),
                Entry(Calm, 3, 1), Entry(Empty, 1, 0),
            },
        };

        Assert.Multiple(() => {
            Assert.That(Request(test, session, 102), Is.EqualTo(new[] { (Busy, 1, (short) 2), (Quiet, 2, (short) 1), (Calm, 3, (short) 3) }));
            Assert.That(Request(test, session, 103), Is.EqualTo(new[] { (Karkar, 1, (short) 1) }));
        });
    }

    [Test]
    public void WithoutTheWorld_NothingIsRanked() {
        TestGameSession test = Setup();
        GameSession session = test.Start();
        Assert.That(Request(test, session, 102), Is.Empty);
    }
}
