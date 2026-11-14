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
}
