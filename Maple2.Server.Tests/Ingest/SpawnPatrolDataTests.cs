using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// A spawn point's PatrolData as the ingest stores it: the MS2PatrolData uuid without dashes, null for the empty guid.
public class SpawnPatrolDataTests {
    [TestCase("6f2c145f-8a4d-4a08-a220-6eab6df9b65b", "6f2c145f8a4d4a08a2206eab6df9b65b")]
    [TestCase("00000000-0000-0000-0000-000000000000", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void SpawnPatrolData(string? patrolData, string? expected) {
        Assert.That(MapEntityMapper.SpawnPatrolData(patrolData), Is.EqualTo(expected));
    }

    // NpcCount defaults to 0 in both spawn point flats. An event point (spawned only by a trigger) at 0 is kept and
    // spawns its list; a plain point at 0 is a switched-off copy and stays out; a point without a known npc stays out.
    [TestCase(true, 0u, 1, ExpectedResult = true)]
    [TestCase(true, 2u, 1, ExpectedResult = true)]
    [TestCase(false, 0u, 1, ExpectedResult = false)]
    [TestCase(false, 1u, 1, ExpectedResult = true)]
    [TestCase(true, 0u, 0, ExpectedResult = false)]
    [TestCase(false, 3u, 0, ExpectedResult = false)]
    public bool KeepsNpcSpawn(bool eventPoint, uint npcCount, int listCount) =>
        MapEntityMapper.KeepsNpcSpawn(eventPoint, npcCount, listCount);
}
