using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The Colosseum's round rewards: a clear gives the reward row (rewardcontent.xml) of every round it added to the
// period's count, read at the room's level, as the room window previews them (uidungeonroomdialog.cpp:1015) and
// DungeonRoom::GetRoundRewardIds(from, to) lists them (commonfiles/dungeonroom.cpp:711). The KR Colosseum
// (24096001) has no exp, meso or box of its own, so these are its only rewards.
public class DungeonRoundRewardTests {
    private const int Room = 24096001;
    private const int RoundGroup = 90002;
    private const short RoomLevel = 70;
    private const int MesoTable = 5;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000013, rewardCount: 13) with {
            Level = RoomLevel, GroupType = DungeonGroupType.colosseum, RoundId = RoundGroup,
        };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.SetTable(new DungeonRoundDataTable(new Dictionary<int, DungeonRoundMetadata[]> {
            [RoundGroup] = [new(901, 0), new(902, 0), new(903, 0), new(904, 0), new(905, 0)],
        }));
        // Each round's row gives its meso from one table that has a value only at the room's level.
        var rows = new Dictionary<int, RewardContentTable.Base>();
        foreach ((int id, float factor) in new[] { (901, 1f), (902, 2f), (903, 3f), (904, 4f), (905, 5f) }) {
            rows[id] = new RewardContentTable.Base(id, MesoTableId: MesoTable, ExpTableId: 0, MesoFactor: factor, ExpFactor: 0f, ItemTableId: 0,
                PrestigeExpTableId: 0);
        }
        test.SetTable(new RewardContentTable(rows, new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long>(),
            new Dictionary<int, Dictionary<int, long>> { [MesoTable] = new() { [RoomLevel] = 1000 } }, new Dictionary<int, long>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
    }

    private void Clear(int round) {
        session.Dungeon.UserRecord!.Round = round;
        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Test]
    public void ClearGivesTheRoundsCleared() {
        long meso = session.Currency.Meso;

        Clear(2);

        Assert.That(session.Currency.Meso - meso, Is.EqualTo(3000));
        Assert.That(session.Dungeon.UserRecord!.Rewards[DungeonRewardType.Meso], Is.EqualTo(3000));
        Assert.That(test.Errors, Is.Empty);
    }

    // A run continued in the period gives only the rounds past the ones it counted already.
    [Test]
    public void ContinuedRunGivesOnlyTheNewRounds() {
        Clear(2);
        long meso = session.Currency.Meso;
        session.Dungeon.UserRecord = new DungeonUserRecord(Room, test.CharacterId);

        Clear(3);

        Assert.That(session.Currency.Meso - meso, Is.EqualTo(3000));
        Assert.That(session.Dungeon.UserRecord!.Rewards[DungeonRewardType.Meso], Is.EqualTo(3000));
        Assert.That(test.Errors, Is.Empty);
    }

    // A shorter new game keeps the period's highest round, so a later long run is not paid its rounds again.
    [Test]
    public void ShorterRunKeepsThePeriodsRounds() {
        Clear(5);
        long meso = session.Currency.Meso;
        session.Dungeon.UserRecord = new DungeonUserRecord(Room, test.CharacterId);
        Clear(2);
        session.Dungeon.UserRecord = new DungeonUserRecord(Room, test.CharacterId);

        Clear(5);

        Assert.That(session.Currency.Meso - meso, Is.EqualTo(0));
        Assert.That(session.Dungeon.Records[Room].UnionClears, Is.EqualTo(5));
        Assert.That(test.Errors, Is.Empty);
    }

    // A second end of the same run (the Colosseum's script loops) is its own result: no earlier reward line, and no
    // clear without a count left.
    [Test]
    public void RepeatedEndStartsANewResult() {
        Clear(2);
        session.Dungeon.Records[Room].UnionClears = 13;
        session.Dungeon.Records[Room].UnionCooldownTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        Clear(2);

        Assert.That(session.Dungeon.UserRecord!.Rewards.GetValueOrDefault(DungeonRewardType.Meso), Is.EqualTo(0));
        Assert.That(session.Dungeon.UserRecord!.Flag.HasFlag(DungeonBonusFlag.Clear), Is.False);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void RoundRewardIds_FollowTheClientsRange() {
        DungeonRoundMetadata[] rounds = [new(901, 0), new(0, 0), new(903, 0)];
        Assert.Multiple(() => {
            Assert.That(Maple2.Model.Validators.DungeonRoomRules.RoundRewardIds(rounds, 0, 3), Is.EqualTo(new[] { 901, 903 }));
            Assert.That(Maple2.Model.Validators.DungeonRoomRules.RoundRewardIds(rounds, 2, 9), Is.EqualTo(new[] { 903 }));
            Assert.That(Maple2.Model.Validators.DungeonRoomRules.RoundRewardIds(rounds, 3, 2), Is.Empty);
        });
    }
}
