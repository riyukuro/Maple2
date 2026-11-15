using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// WorldChampion 8 (the season window, uiworldchampionrewarddialog.cpp:277-282) and 9 (its move button, :381-386).
public class WorldChampionHandlerTests {
    private TestGameSession test = null!;

    private static WorldChampionTable.Season Season(int id, DateTime begin) {
        return new WorldChampionTable.Season(id, 2000423, 100, begin, begin.AddDays(3), 1500, 180510006, [
            new WorldChampionTable.Step(1, 23500110, 20000001),
            new WorldChampionTable.Step(2, 23500111, 20000002),
        ]);
    }

    private void Start(int mapId, params WorldChampionTable.Season[] seasons) => Start(mapId, true, seasons);

    private void Start(int mapId, bool featureOn, params WorldChampionTable.Season[] seasons) {
        test = new TestGameSession();
        if (!featureOn) {
            test.SetFeaturesOff(WorldChampionRules.Feature);
        }
        test.SetServerTable(new WorldChampionTable(seasons.ToDictionary(season => season.Id)));
        test.Start();
        test.EnterMap(TestGameSession.Map(mapId));
        test.TakeSent();
    }

    private List<byte[]> Send(byte mode) {
        new WorldChampionHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(mode)));
        return test.TakeSent();
    }

    // The season on, at the step the World server counts.
    [Test]
    public void Open_SeasonOn() {
        WorldChampionTable.Season season = Season(3, DateTime.Now.AddHours(-1));
        Start(2000062, season);
        test.World.WorldChampion = new WorldChampionInfo { Season = 3, Step = 2, StepDamage = 100 };
        Assert.That(Send(WorldChampionHandler.OpenMode), Is.EqualTo(new[] { WorldChampionPacket.Season(season, 2).ToArray() }));
    }

    // The World server does not answer: the season on shows its first step.
    [Test]
    public void Open_WorldDown() {
        WorldChampionTable.Season season = Season(3, DateTime.Now.AddHours(-1));
        Start(2000062, season);
        test.World.TimeEventError = 1;
        Assert.That(Send(WorldChampionHandler.OpenMode), Is.EqualTo(new[] { WorldChampionPacket.Season(season, 1).ToArray() }));
    }

    [Test]
    public void Open_NextSeason() {
        WorldChampionTable.Season next = Season(4, DateTime.Now.AddDays(2));
        Start(2000062, Season(3, DateTime.Now.AddDays(-9)), next);
        test.World.WorldChampion = new WorldChampionInfo();
        Assert.That(Send(WorldChampionHandler.OpenMode), Is.EqualTo(new[] { WorldChampionPacket.NextSeason(next.Begin).ToArray() }));
    }

    // No season in the table: the notice.
    [Test]
    public void Open_NoSeason() {
        Start(2000062);
        test.World.WorldChampion = new WorldChampionInfo();
        Assert.That(Send(WorldChampionHandler.OpenMode), Is.EqualTo(new[] { WorldChampionPacket.NoSeason().ToArray() }));
    }

    // Every season of the table has ended: the last one's week repeats, the next id on (WorldChampionRules.Find).
    [Test]
    public void Open_RolledSeason() {
        // One clock read: two DateTime.Now calls across a second boundary gave the expected week a second late.
        DateTime begin = DateTime.Now.AddDays(-9);
        Start(2000062, Season(3, begin));
        test.World.WorldChampion = new WorldChampionInfo { Season = 4, Step = 2 };
        WorldChampionTable.Season rolled = Season(3, begin) with { Id = 4 };
        rolled = rolled with { Begin = rolled.Begin.AddDays(7), End = rolled.End.AddDays(7) };
        byte[] sent = Send(WorldChampionHandler.OpenMode).Single();
        Assert.That(sent[3..7], Is.EqualTo(BitConverter.GetBytes(4)));
        Assert.That(sent[7..15], Is.EqualTo(BitConverter.GetBytes(new DateTimeOffset(rolled.Begin).ToUnixTimeSeconds())));
        Assert.That(sent[15..23], Is.EqualTo(BitConverter.GetBytes(new DateTimeOffset(rolled.End).ToUnixTimeSeconds())));
        Assert.That(sent[24], Is.EqualTo(2));
    }

    // No season on: the notice; on the season's field already: nothing.
    [Test]
    public void Move_NoSeasonOrThere() {
        Start(2000062);
        Assert.That(Send(WorldChampionHandler.MoveMode), Is.EqualTo(new[] { WorldChampionPacket.NoSeason().ToArray() }));
        Start(2000423, Season(3, DateTime.Now.AddHours(-1)));
        Assert.That(Send(WorldChampionHandler.MoveMode), Is.Empty);
    }

    // The WorldChampionMushroom feature off (feature 199: the client shows no menu entry, gameeventmanagercli.cpp:936):
    // no season and no move.
    [Test]
    public void FeatureOff() {
        Start(2000062, false, Season(3, DateTime.Now.AddHours(-1)));
        test.World.WorldChampion = new WorldChampionInfo { Season = 3, Step = 1 };
        Assert.Multiple(() => {
            Assert.That(Send(WorldChampionHandler.OpenMode), Is.EqualTo(new[] { WorldChampionPacket.NoSeason().ToArray() }));
            Assert.That(Send(WorldChampionHandler.MoveMode), Is.EqualTo(new[] { WorldChampionPacket.NoSeason().ToArray() }));
        });
    }

    // The world map icon: the npc of the step the World server counts on the season's field, type 2, channel 0.
    [Test]
    public void MapIcon_TheStepsNpc() {
        WorldChampionTable.Season season = Season(3, DateTime.Now.AddHours(-1));
        Start(2000062, season);
        test.World.WorldChampion = new WorldChampionInfo { Season = 3, Step = 2 };
        var icon = new MapWorldBoss(23500111, 2000423, 0, new DateTimeOffset(season.Begin).ToUnixTimeSeconds(), MapWorldBoss.WorldChampionType);
        Assert.That(WorldChampionHandler.MapIcon(test.Session), Is.EqualTo(icon));

        // RequestWorldmap 0 (the world map's load) adds it as its own group.
        new WorldMapHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteInt(102);
        }));
        Assert.That(test.TakeSent(), Is.EqualTo(new[] { WorldMapPacket.Load([new[] { icon }], []).ToArray() }));
    }

    // The world map's boss icons go only in the answer to the map's request (KMS2 sends none at login): the alive
    // boss, one row per channel it is alive on, then the champion's icon as its own group.
    [Test]
    public void MapLoad_AliveBossesAndTheIcon() {
        WorldChampionTable.Season season = Season(3, DateTime.Now.AddHours(-1));
        Start(2000062, season);
        test.World.WorldChampion = new WorldChampionInfo { Season = 3, Step = 2 };
        var boss = new WorldBossMetadata(1000, 100, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1), TimeSpan.FromHours(1), TimeSpan.Zero,
            TimeSpan.FromMinutes(5), [2000084], [100], [23000013], 1000, true, false, 1, false, false);
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata> { [boss.Id] = boss }));
        var alive = new TimeEventResponse.Types.ActiveWorldBoss { MetadataId = boss.Id, SpawnTimestamp = 1700000000 };
        alive.AliveChannels.AddRange([1, 2]);
        test.World.ActiveWorldBosses.Add(alive);
        var icon = new MapWorldBoss(23500111, 2000423, 0, new DateTimeOffset(season.Begin).ToUnixTimeSeconds(), MapWorldBoss.WorldChampionType);

        new WorldMapHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteInt(102);
        }));
        Assert.That(test.TakeSent(), Is.EqualTo(new[] {
            WorldMapPacket.Load([
                new[] { new MapWorldBoss(23000013, 2000084, 1, 1700000000), new MapWorldBoss(23000013, 2000084, 2, 1700000000) },
                new[] { icon },
            ], []).ToArray(),
        }));
    }

    // The World down (the call throws): the map's request is still answered, with no boss.
    [Test]
    public void MapLoad_WorldDown_AnswersNoBoss() {
        Start(2000062);
        test.World.TimeEventFailure = new RpcException(new Status(StatusCode.Unavailable, "no world"));

        Assert.DoesNotThrow(() => new WorldMapHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteInt(102);
        })));
        Assert.That(test.TakeSent(), Is.EqualTo(new[] { WorldMapPacket.Load([], []).ToArray() }));
    }

    // The World server down: the first step's npc.
    [Test]
    public void MapIcon_WorldDown() {
        Start(2000062, Season(3, DateTime.Now.AddHours(-1)));
        test.World.TimeEventError = 1;
        Assert.That(WorldChampionHandler.MapIcon(test.Session)?.NpcId, Is.EqualTo(23500110));
    }

    // No icon: the season cleared, no season on, or the feature off.
    [Test]
    public void MapIcon_None() {
        Start(2000062, Season(3, DateTime.Now.AddHours(-1)));
        test.World.WorldChampion = new WorldChampionInfo { Season = 3, Step = 3 };
        Assert.That(WorldChampionHandler.MapIcon(test.Session), Is.Null);
        Start(2000062);
        Assert.That(WorldChampionHandler.MapIcon(test.Session), Is.Null);
        Start(2000062, false, Season(3, DateTime.Now.AddHours(-1)));
        Assert.That(WorldChampionHandler.MapIcon(test.Session), Is.Null);
    }
}
