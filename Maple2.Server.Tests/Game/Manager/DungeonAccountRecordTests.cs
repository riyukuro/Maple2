using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// An account reward room (dungeonroom.xml isAccountReward, commonfiles/dungeonroom.cpp:384; the room window adds
// s_dungeonRoom_reward_account to its daily and weekly counts, uidungeonroomdialog.cpp:887-909) counts its rewards for
// the account: every character of it sees and takes the same counts. The client never reads the flag for the limit
// (DungeonRecord::IsAccountReward has no caller), so the server keeps it.
public class DungeonAccountRecordTests {
    private const int AccountRoom = 29006001;
    private const int CharacterRoom = 29006002;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long now;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private void Rooms(bool accountWide) {
        DungeonRoomMetadata account = TestGameSession.DungeonRoom(AccountRoom, 50000013, rewardCount: 10);
        account = account with { Reward = account.Reward with { AccountWide = accountWide, SubRewardCount = 2 } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [AccountRoom] = account,
            [CharacterRoom] = TestGameSession.DungeonRoom(CharacterRoom, 50000013, rewardCount: 10),
        }));
    }

    private void Took(DungeonRecord record, byte clears, byte subClears, long end) {
        record.UnionClears = clears;
        record.UnionSubClears = subClears;
        record.UnionCooldownTimestamp = end;
        record.UnionSubCooldownTimestamp = end;
    }

    private void SaveMain() {
        using GameStorage.Request db = test.Storage.Context();
        session.Dungeon.Save(db);
    }

    private long CreateAlt() {
        using GameStorage.Request db = test.Storage.Context();
        var alt = new Character {
            AccountId = test.AccountId,
            Name = Guid.NewGuid().ToString("N")[..12],
            Job = Job.Newbie,
            Mastery = new Mastery(),
            MapId = 2000023,
            ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        };
        return db.CreateCharacter(alt)?.Id ?? throw new InvalidOperationException("alt not created");
    }

    // The alt's dungeon records as a login loads them.
    private DungeonManager LoginAs(long characterId) {
        typeof(GameSession).GetProperty(nameof(GameSession.CharacterId))!.SetValue(session, characterId);
        return new DungeonManager(session);
    }

    [Test]
    public void AnAltSeesTheCountsTheMainTook() {
        Rooms(accountWide: true);
        test.Start();
        Took(session.Dungeon.Records[AccountRoom], 3, 2, now + 3600);
        Took(session.Dungeon.Records[CharacterRoom], 3, 2, now + 3600);
        SaveMain();

        DungeonManager alt = LoginAs(CreateAlt());

        Assert.Multiple(() => {
            Assert.That((alt.Records[AccountRoom].UnionClears, alt.Records[AccountRoom].UnionSubClears), Is.EqualTo((3, 2)));
            Assert.That(alt.Records[AccountRoom].UnionCooldownTimestamp, Is.EqualTo(now + 3600));
            Assert.That((alt.Records[CharacterRoom].UnionClears, alt.Records[CharacterRoom].UnionSubClears), Is.EqualTo((0, 0)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // What the alt takes counts for the main too.
    [Test]
    public void TheMainSeesTheCountsTheAltTook() {
        Rooms(accountWide: true);
        test.Start();
        long main = test.CharacterId;
        DungeonManager alt = LoginAs(CreateAlt());
        Took(alt.Records[AccountRoom], 4, 1, now + 3600);
        using (GameStorage.Request db = test.Storage.Context()) {
            alt.Save(db);
        }

        DungeonManager back = LoginAs(main);

        Assert.That(back.Records[AccountRoom].UnionClears, Is.EqualTo(4));
        Assert.That(test.Errors, Is.Empty);
    }

    // The rows the characters kept before the room counted for the account: the first account count is what they
    // took in their periods, so the switch gives no character a fresh limit. A row whose period is over adds nothing.
    [Test]
    public void TheFirstAccountCountKeepsTheCharactersRows() {
        Rooms(accountWide: false);
        test.Start();
        Took(session.Dungeon.Records[AccountRoom], 3, 1, now + 3600);
        SaveMain();
        long altId = CreateAlt();
        DungeonManager alt = LoginAs(altId);
        Took(alt.Records[AccountRoom], 2, 1, now + 7200);
        using (GameStorage.Request db = test.Storage.Context()) {
            alt.Save(db);
        }
        long expiredId = CreateAlt();
        DungeonManager expired = LoginAs(expiredId);
        Took(expired.Records[AccountRoom], 9, 9, now - 60);
        using (GameStorage.Request db = test.Storage.Context()) {
            expired.Save(db);
        }

        Rooms(accountWide: true);
        DungeonManager after = LoginAs(altId);

        Assert.Multiple(() => {
            Assert.That((after.Records[AccountRoom].UnionClears, after.Records[AccountRoom].UnionSubClears), Is.EqualTo((5, 2)));
            Assert.That(after.Records[AccountRoom].UnionCooldownTimestamp, Is.EqualTo(now + 7200));
            // With 2 of 2 timed clears taken, the alt has no count left today.
            Assert.That(Maple2.Model.Validators.DungeonRoomRules.HasCountLeft(after.Records[AccountRoom], 10, 2, now), Is.False);
            Assert.That(test.Errors, Is.Empty);
        });
        // The account count is made once; the main then reads it, not its own row.
        Assert.That(LoginAs(test.CharacterId).Records[AccountRoom].UnionClears, Is.EqualTo(5));
    }

    [Test]
    public void MergeAccountCounts_SumsTheRowsInTheirPeriods() {
        var target = new DungeonRecord(AccountRoom);
        var rows = new[] {
            new DungeonRecord(AccountRoom) { UnionClears = 200, ExtraSubClears = 1, UnionCooldownTimestamp = now + 10, UnionSubClears = 1, UnionSubCooldownTimestamp = now + 5 },
            new DungeonRecord(AccountRoom) { UnionClears = 100, ExtraSubClears = 2, UnionCooldownTimestamp = now + 20, UnionSubClears = 4, UnionSubCooldownTimestamp = now - 5 },
            new DungeonRecord(AccountRoom) { UnionClears = 7, UnionCooldownTimestamp = now, ExtraClears = 3, ExtraCooldownTimestamp = now + 1 },
        };

        Maple2.Model.Validators.DungeonRoomRules.MergeAccountCounts(target, rows, now);

        Assert.Multiple(() => {
            Assert.That((target.UnionClears, target.ExtraSubClears, target.UnionCooldownTimestamp), Is.EqualTo((255, 3, now + 20)));
            Assert.That((target.UnionSubClears, target.UnionSubCooldownTimestamp), Is.EqualTo((1, now + 5)));
            Assert.That((target.ExtraClears, target.ExtraCooldownTimestamp), Is.EqualTo((3, now + 1)));
        });
    }
}
