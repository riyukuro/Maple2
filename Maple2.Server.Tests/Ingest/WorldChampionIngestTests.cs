using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/Server/<locale>/WorldChampionBase.xml (KR and NA 12 seasons, CN 7): a season's field, spawn point, active
// time, reward damage, system mail, and its steps (npc, reward content row). KR season 1: field 2000423, spawn
// point 100, 2018-05-11-00-00-00 to 2018-05-13-23-59-59, rewardDamage 1500000, systemMailID 180510006, steps
// 23500110 (20000001) on.
public class WorldChampionIngestTests {
    private static XmlDocument Document(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    [Test]
    public void Season_KR1() {
        WorldChampionTable table = ServerTableMapper.MapWorldChampion(Document("""
            <ms2>
              <WorldChampion seasonID="1" fieldID="2000423" spawnPointID="100" activeBegin="2018-05-11-00-00-00" activeEnd="2018-05-13-23-59-59" rewardDamage="1500000" systemMailID="180510006">
                <Step StepID="2" npcID="23500111" rewardID="20000002" />
                <Step StepID="1" npcID="23500110" rewardID="20000001" />
              </WorldChampion>
            </ms2>
            """), _ => true);
        WorldChampionTable.Season season = table.Entries[1];
        Assert.Multiple(() => {
            Assert.That(season.MapId, Is.EqualTo(2000423));
            Assert.That(season.SpawnPointId, Is.EqualTo(100));
            Assert.That(season.Begin, Is.EqualTo(new DateTime(2018, 5, 11, 0, 0, 0)));
            Assert.That(season.End, Is.EqualTo(new DateTime(2018, 5, 13, 23, 59, 59)));
            Assert.That(season.RewardDamage, Is.EqualTo(1500000));
            Assert.That(season.SystemMailId, Is.EqualTo(180510006));
            Assert.That(season.Steps, Is.EqualTo(new[] {
                new WorldChampionTable.Step(1, 23500110, 20000001),
                new WorldChampionTable.Step(2, 23500111, 20000002),
            }));
        });
    }

    // No readable time, an end before the begin, no step, or a row not available is left out; a step without an npc
    // is dropped.
    [Test]
    public void Season_BadRowsLeftOut() {
        WorldChampionTable table = ServerTableMapper.MapWorldChampion(Document("""
            <ms2>
              <WorldChampion seasonID="1" fieldID="2000423" activeBegin="" activeEnd="2018-05-13-23-59-59"><Step StepID="1" npcID="23500110" rewardID="20000001" /></WorldChampion>
              <WorldChampion seasonID="2" fieldID="2000423" activeBegin="2018-05-14-00-00-00" activeEnd="2018-05-13-23-59-59"><Step StepID="1" npcID="23500110" rewardID="20000001" /></WorldChampion>
              <WorldChampion seasonID="3" fieldID="2000423" activeBegin="2018-05-11-00-00-00" activeEnd="2018-05-13-23-59-59"><Step StepID="1" npcID="0" rewardID="20000001" /></WorldChampion>
              <WorldChampion seasonID="4" fieldID="2000423" activeBegin="2018-05-11-00-00-00" activeEnd="2018-05-13-23-59-59" feature="Off"><Step StepID="1" npcID="23500110" rewardID="20000001" /></WorldChampion>
              <WorldChampion seasonID="5" fieldID="2000423" activeBegin="2018-05-11-00-00-00" activeEnd="2018-05-13-23-59-59">
                <Step StepID="1" npcID="23500110" rewardID="20000001" />
                <Step StepID="2" npcID="0" rewardID="20000002" />
              </WorldChampion>
            </ms2>
            """), row => row.GetAttribute("feature") != "Off");
        Assert.That(table.Entries.Keys, Is.EqualTo(new[] { 5 }));
        Assert.That(table.Entries[5].Steps, Has.Count.EqualTo(1));
    }

    private static GameEvent Event(int id, string type, string start) => new() {
        gameEventID = id,
        eventType = type,
        eventStart = start,
    };

    // MS2_SERVER_OVERLAY's table/Server/NA/GameEvent.xml: a row takes the place of the pack's row of its id, in the
    // pack's order; a new id is added after the pack's rows.
    [Test]
    public void GameEventOverlay_ReplacesById() {
        (int, GameEvent)[] pack = [
            (120081, Event(120081, "DailyLoginReward", "2018-05-10-09-00")),
            (500009, Event(500009, "WorldmapIcon", "2018-05-11-00-00")),
            (500010, Event(500010, "WorldmapIcon", "2018-05-18-00-00")),
        ];
        GameEvent[] overlay = [Event(500009, "WorldmapIcon", "2026-11-13-00-00"), Event(999001, "WorldmapIcon", "2026-11-20-00-00")];
        var merged = ServerOverlay.MergeGameEvents(pack, overlay).ToList();
        Assert.Multiple(() => {
            Assert.That(merged.Select(row => row.Id), Is.EqualTo(new[] { 120081, 500009, 500010, 999001 }));
            Assert.That(merged[1].GameEvent.eventStart, Is.EqualTo("2026-11-13-00-00"));
            Assert.That(merged[2].GameEvent.eventStart, Is.EqualTo("2018-05-18-00-00"));
            Assert.That(merged[3].GameEvent.eventStart, Is.EqualTo("2026-11-20-00-00"));
        });
    }

    // The overlay file is read the way the parser reads the pack's: the rows of the locale.
    [Test]
    public void GameEventOverlay_Parses() {
        IReadOnlyList<GameEvent> rows = ServerOverlay.ParseGameEvents("""
            <?xml version="1.0" encoding="utf-8"?>
            <ms2>
              <gameEvent gameEventID="500009" eventType="WorldmapIcon" eventStart="2026-11-13-00-00" eventEnd="2026-11-15-23-59" value1="x" />
              <gameEvent gameEventID="120081" eventType="DailyLoginReward" eventStart="2026-11-12-09-00" eventEnd="2026-11-15-23-59-59" value1="180510005" />
            </ms2>
            """);
        Assert.That(rows.Select(row => (row.gameEventID, row.eventType, row.eventStart)), Is.EqualTo(new[] {
            (500009, "WorldmapIcon", "2026-11-13-00-00"),
            (120081, "DailyLoginReward", "2026-11-12-09-00"),
        }));
    }

    // Unset, nothing is read; set to a directory that is not there, the ingest stops.
    [Test]
    public void Overlay_UnsetOrMissing() {
        Assert.That(ServerOverlay.None.Read(ServerOverlay.GameEventPath), Is.Null);
        string missing = Path.Combine(Path.GetTempPath(), $"ms2-overlay-missing-{Guid.NewGuid():N}");
        string? old = Environment.GetEnvironmentVariable(ServerOverlay.Option);
        try {
            Environment.SetEnvironmentVariable(ServerOverlay.Option, missing);
            Assert.Throws<DirectoryNotFoundException>(() => ServerOverlay.FromEnvironment());
            Environment.SetEnvironmentVariable(ServerOverlay.Option, "");
            Assert.That(ServerOverlay.FromEnvironment().Root, Is.Null);
        } finally {
            Environment.SetEnvironmentVariable(ServerOverlay.Option, old);
        }
    }

    [Test]
    public void Overlay_ReadsTheFile() {
        string root = Path.Combine(Path.GetTempPath(), $"ms2-overlay-{Guid.NewGuid():N}");
        string file = Path.Combine(root, "table", "Server", "KR", "WorldChampionBase.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try {
            System.IO.File.WriteAllText(file, "<ms2 />");
            var overlay = new ServerOverlay(root);
            Assert.That(overlay.Read("table/Server/KR/WorldChampionBase.xml"), Is.EqualTo("<ms2 />"));
            Assert.That(overlay.Read("table/Server/NA/WorldChampionBase.xml"), Is.Null);
        } finally {
            Directory.Delete(root, true);
        }
    }
}
