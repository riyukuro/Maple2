using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The room list's enter limit of a dungeon room (DungeonManager.GetEnterLimit), in the order the client's room
// window shows them: level, trophy, gear score, required clears, then the veteran flag, else rookie.
public class DungeonEnterLimitTests {
    private const int Room = 23000001;
    private const int Prior = 23000002;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(DungeonRoomLimitMetadata limit, int maxRevivals = 0) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000301);
        room = room with { Limit = limit with { MaxRevivalCount = maxRevivals } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [Room] = room,
            [Prior] = TestGameSession.DungeonRoom(Prior, 90000302),
        }));
        test.Start();
    }

    private static DungeonRoomLimitMetadata Limit(int minLevel = 1, int gearScore = 0, int achievement = 0, int[]? clears = null) =>
        TestGameSession.DungeonRoom(Room, 0).Limit with { MinLevel = minLevel, GearScore = gearScore, RequiredAchievementId = achievement, ClearDungeonIds = clears ?? [] };

    private DungeonEnterLimit Current => session.Player.Value.Character.DungeonEnterLimits[Room];

    [Test]
    public void BelowTheMinLevel_TheLevelLimit() {
        Start(Limit(minLevel: 99));
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.MinLevel));
    }

    [Test]
    public void ARoomWithoutLimits_Rookie_ThenVeteranOnceTheFlagIsSet() {
        Start(Limit());
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Rookie));

        session.Dungeon.Records[Room].Flag |= DungeonRecordFlag.Veteran;
        session.Dungeon.UpdateDungeonEnterLimit();
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Veteran));
    }

    [Test]
    public void ARequiredTrophyThePlayerLacks_TheAchievementLimit() {
        Start(Limit(achievement: 12345));
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Achievement));
    }

    [Test]
    public void AGearScoreAboveThePlayers_TheGearscoreLimit() {
        Start(Limit(gearScore: 999999));
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Gearscore));
    }

    [Test]
    public void ARequiredClearNotDone_ThenDone() {
        Start(Limit(clears: [Prior]));
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.DungeonClear));

        session.Dungeon.Records[Prior].ClearTimestamp = 1_700_000_000;
        session.Dungeon.UpdateDungeonEnterLimit();
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Rookie));
    }

    [Test]
    public void UpdateSendsTheEntranceListOnlyWhenALimitChanged() {
        Start(Limit(clears: [Prior]));
        test.TakeSent();

        session.Dungeon.UpdateDungeonEnterLimit();
        Assert.That(test.TakeSent(), Is.Empty);

        session.Dungeon.Records[Prior].ClearTimestamp = 1_700_000_000;
        session.Dungeon.UpdateDungeonEnterLimit();
        Assert.That(test.TakeSent(), Is.Not.Empty);
    }

    // defaultRevivalLimitCount 0 is no limit; with a limit the free revivals end when the counted ones reach it.
    [Test]
    public void DefaultRevivals_CountedAgainstTheRoomsLimit() {
        Start(Limit(), maxRevivals: 2);
        test.EnterDungeon(session.TableMetadata.DungeonRoomTable.Entries[Room]);
        test.TakeSent();

        Assert.That(session.Dungeon.HasDefaultRevivalLeft(), Is.True);
        session.Dungeon.CountDefaultRevival();
        Assert.That(session.Dungeon.HasDefaultRevivalLeft(), Is.True);
        session.Dungeon.CountDefaultRevival();
        Assert.That(session.Dungeon.HasDefaultRevivalLeft(), Is.False);
        Assert.That(session.Dungeon.UserRecord!.RevivalCount, Is.EqualTo(2));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(2));
    }

    [Test]
    public void DefaultRevivals_NoLimitIsNeverUsedUp() {
        Start(Limit(), maxRevivals: 0);
        test.EnterDungeon(session.TableMetadata.DungeonRoomTable.Entries[Room]);
        for (int i = 0; i < 5; i++) {
            session.Dungeon.CountDefaultRevival();
        }
        Assert.That(session.Dungeon.HasDefaultRevivalLeft(), Is.True);
    }

    [Test]
    public void DefaultRevivals_OutsideADungeonThereIsNoLimit() {
        Start(Limit(), maxRevivals: 1);
        Assert.That(session.Dungeon.HasDefaultRevivalLeft(), Is.True);
    }
}
