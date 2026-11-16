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

    // The session starts on a room without limits (the managers a limit reads are built after the dungeon manager), then
    // the room gets its limit and the limits are worked out again.
    private void Relimit(DungeonRoomLimitMetadata limit) {
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Config, session);
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [Room] = TestGameSession.DungeonRoom(Room, 90000301) with { Limit = limit },
        }));
        session.Dungeon.UpdateDungeonEnterLimit();
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

    // A room for premium club members only: the vip limit for a player without the club time.
    [Test]
    public void AVipOnlyRoomForAPlayerWithoutTheClub_TheVipLimit() {
        Start(Limit());
        Relimit(Limit() with { VipOnly = true });
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Vip));

        session.Player.Value.Account.PremiumTime = System.DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        session.Dungeon.UpdateDungeonEnterLimit();
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Rookie));
    }

    // A room that asks for a buff the player does not hold: the buff limit.
    [Test]
    public void ARequiredBuffThePlayerLacks_TheBuffLimit() {
        Start(Limit());
        Relimit(Limit() with { Buffs = [70000001] });
        Assert.That(Current, Is.EqualTo(DungeonEnterLimit.Buff));
    }

    // The recommended weapon of the job: the right weapon type of the job in the right hand gives no limit, any other or
    // none gives the weapon limit. Weapon item ids are 1 (group), the type, then the number (ItemType).
    [TestCase(Job.Newbie, 30)]
    [TestCase(Job.Knight, 32)]
    [TestCase(Job.Berserker, 50)]
    [TestCase(Job.Wizard, 52)]
    [TestCase(Job.Priest, 33)]
    [TestCase(Job.Archer, 51)]
    [TestCase(Job.HeavyGunner, 53)]
    [TestCase(Job.Assassin, 34)]
    [TestCase(Job.Thief, 31)]
    [TestCase(Job.RuneBlader, 54)]
    [TestCase(Job.Striker, 55)]
    [TestCase(Job.SoulBinder, 56)]
    public void TheRecommendedWeaponOfTheJob(Job job, int weaponType) {
        // The wrong weapon is a type of another job: a bludgeon, or a dagger for the newbie's own.
        int wrongType = weaponType == 30 ? 31 : 30;
        foreach ((int type, bool expectsLimit) in new[] { (weaponType, false), (wrongType, true), (0, true) }) {
            test = new TestGameSession();
            test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
                [Room] = TestGameSession.DungeonRoom(Room, 90000301),
            }));
            if (type != 0) {
                test.Store(TestGameSession.Metadata(10_000_000 + type * 100_000 + 1, EquipSlot.RH, type: 1), group: ItemGroup.Gear, slot: EquipSlot.RH);
            }
            test.Start();
            session.Player.Value.Character.Job = job;
            Relimit(Limit() with { EquippedRecommendedWeapon = true });

            Assert.That(Current, expectsLimit ? Is.EqualTo(DungeonEnterLimit.RecommendedWeapon) : Is.Not.EqualTo(DungeonEnterLimit.RecommendedWeapon),
                $"{job} with weapon type {type}");
        }
    }
}
