using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// What a room's clear keeps for the ranking dialog (DungeonFieldManager.ChangeState): a boss room's clear goes on its
// boss board once, with the room's players in the field; a Colosseum room's first user keeps its rounds as the best
// run of the current Colosseum season. The room ids, board ids and seasons are made up.
public class DungeonBoardClearTests {
    private const int Room = 90000400;
    private const int Board = 9001;
    private const int Season = 205101;

    private TestGameSession test = null!;
    private DungeonRoomRecord record = null!;
    private DungeonFieldManager Field => (DungeonFieldManager) test.Session.Field!;

    private void Enter(DungeonGroupType group, int bossRanking = Board, bool inSeason = true) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000401) with { GroupType = group, BossRanking = bossRanking };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        var empty = new Dictionary<int, SeasonDataTable.Entry>();
        var boss = new Dictionary<int, SeasonDataTable.Entry>();
        if (inSeason) {
            boss[Season] = new SeasonDataTable.Entry(Season, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1), []);
        }
        test.SetTable(new SeasonDataTable(empty, boss, empty, empty, empty, empty, empty, empty, empty));
        test.SetTable(new DungeonRoundDataTable(new Dictionary<int, DungeonRoundMetadata[]>()));
        test.Start();
        record = test.EnterDungeon(room);
        // The field's result log (the test field is built without its field initializers).
        typeof(DungeonFieldManager).GetField("resultLogger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(Field, Serilog.Log.Logger);
        record.UserResults[test.CharacterId] = test.Session.Dungeon.UserRecord!;
        record.SetFirstUser(test.CharacterId);
        // A player in no party.
        test.Session.Party = (PartyManager) RuntimeHelpers.GetUninitializedObject(typeof(PartyManager));
        test.TakeSent();
    }

    private List<(int RankingId, int DungeonId, long LeaderId, long[] Members, long Damage)> BossClears() {
        using var context = new Ms2Context(test.Options);
        return context.BossClear.AsNoTracking().Include(clear => clear.Members).AsEnumerable()
            .Select(clear => (clear.RankingId, clear.DungeonId, clear.LeaderId, clear.Members.Select(member => member.CharacterId).ToArray(), clear.Damage))
            .ToList();
    }

    private ColosseumBest? Best() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetColosseumBest(test.CharacterId, Season);
    }

    // A raid's clear: the board gets the clear, led by the room's first user (no party), with the boss damage.
    [Test]
    public void RaidClear_GoesOnTheBossBoard() {
        Enter(DungeonGroupType.raid);
        record.AddBossDamage(test.CharacterId, 1234);

        Field.ChangeState(DungeonState.Clear);

        Assert.That(BossClears(), Is.EqualTo(new[] { (Board, Room, test.CharacterId, new[] { test.CharacterId }, 1234L) }).AsCollection);
    }

    // The board takes one clear of the room.
    [Test]
    public void RaidClear_IsKeptOnce() {
        Enter(DungeonGroupType.raid);
        Field.ChangeState(DungeonState.Clear);
        record.State = DungeonState.None;

        Field.ChangeState(DungeonState.Clear);

        Assert.That(BossClears(), Has.Count.EqualTo(1));
    }

    // Nobody of the room in the field: nothing is kept.
    [Test]
    public void RaidClear_WithNoPlayerOfTheRoomHere_KeepsNothing() {
        Enter(DungeonGroupType.raid);
        record.UserResults.Clear();
        record.UserResults[test.CharacterId + 1] = new DungeonUserRecord(Room, test.CharacterId + 1);

        Field.ChangeState(DungeonState.Clear);

        Assert.That(BossClears(), Is.Empty);
    }

    // A room without a board, or of a group without boss boards, keeps no clear.
    [TestCase(DungeonGroupType.raid, 0)]
    [TestCase(DungeonGroupType.normal, Board)]
    public void Clear_OfARoomWithoutABossBoard_KeepsNothing(DungeonGroupType group, int bossRanking) {
        Enter(group, bossRanking);

        Field.ChangeState(DungeonState.Clear);

        Assert.That(BossClears(), Is.Empty);
    }

    // A fail keeps no clear.
    [Test]
    public void Fail_KeepsNothing() {
        Enter(DungeonGroupType.raid);

        Field.ChangeState(DungeonState.Fail);

        Assert.That(BossClears(), Is.Empty);
    }

    // A Colosseum run with rounds cleared is the first user's best of the season.
    [Test]
    public void ColosseumClear_KeepsTheRunForTheSeason() {
        Enter(DungeonGroupType.colosseum);
        record.UserResults[test.CharacterId].Round = 7;

        Field.ChangeState(DungeonState.Clear, showResult: false);

        Assert.That(Best()?.Rounds, Is.EqualTo(7));
    }

    // Outside every Colosseum season the run is not kept.
    [Test]
    public void ColosseumClear_OutsideASeason_KeepsNothing() {
        Enter(DungeonGroupType.colosseum, inSeason: false);
        record.UserResults[test.CharacterId].Round = 7;

        Field.ChangeState(DungeonState.Clear, showResult: false);

        Assert.That(Best(), Is.Null);
    }

    // No round cleared, or the script disabled the ranking: nothing is kept.
    [TestCase(0, false)]
    [TestCase(7, true)]
    public void ColosseumClear_NoRoundsOrRankingDisabled_KeepsNothing(int rounds, bool disabled) {
        Enter(DungeonGroupType.colosseum);
        record.UserResults[test.CharacterId].Round = rounds;
        if (disabled) {
            record.DisableRanking();
        }

        Field.ChangeState(DungeonState.Clear, showResult: false);

        Assert.That(Best(), Is.Null);
    }
}
