using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;

namespace Maple2.Server.Tests.Core;

// A channel's full record (SetFields with every field) applied to another server's copy makes the two equal, and a
// second apply of the same request changes nothing; each field alone is seen as its own change.
public class PlayerInfoRoundTripTests {
    private static PlayerInfo Blank() {
        return new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
    }

    private static PlayerInfo Full() {
        PlayerInfo info = new PlayerInfo(new CharacterInfo(1, 2, "b", "motto", "pic", Gender.Male, Job.Knight, 30), "home",
            new AchievementInfo { Combat = 3, Adventure = 4, Lifestyle = 5 }, [7, 8]) {
            GearScore = 900, CurrentHp = 50, TotalHp = 100, DeathState = DeathState.FirstDeath,
            MapId = 2000023, RoomId = 6, DungeonId = 9, Channel = 2, LastOnlineTime = 11,
            GuildId = 12, GuildName = "guild", MentorRole = MentorRole.Mentor, AccountAdminPermissions = AdminPermissions.Find,
            PlotMapId = 62000000, PlotNumber = 13, ApartmentNumber = 14, PlotExpiryTime = 15, PremiumTime = 16,
            DungeonEnterLimits = new Dictionary<int, DungeonEnterLimit> { [1] = DungeonEnterLimit.Rookie },
        };
        return info;
    }

    private static object[] Fields(PlayerInfo info) => [
        info.Name, info.Motto, info.Picture, info.Job, info.Level, info.GearScore, info.CurrentHp, info.TotalHp, info.DeathState,
        info.MapId, info.RoomId, info.DungeonId, info.Channel, info.LastOnlineTime, info.GuildId, info.GuildName, info.MentorRole,
        info.AccountAdminPermissions, info.HomeName, info.PlotMapId, info.PlotNumber, info.ApartmentNumber, info.PlotExpiryTime,
        info.PremiumTime, info.AchievementInfo.Combat, info.AchievementInfo.Adventure, info.AchievementInfo.Lifestyle,
        string.Join(",", info.ClubIds), string.Join(",", info.DungeonEnterLimits),
    ];

    private static PlayerUpdateRequest Request(PlayerInfo info) {
        var request = new PlayerUpdateRequest { AccountId = info.AccountId, CharacterId = info.CharacterId };
        request.SetFields(UpdateField.All, info);
        return request;
    }

    [Test]
    public void FullRecord_MakesTheCopyEqual_ThenNoChange() {
        PlayerInfo copy = Blank();
        PlayerUpdateRequest request = Request(Full());

        PlayerInfoUpdateEvent? update = copy.ApplyChanged(request);

        Assert.Multiple(() => {
            Assert.That(update, Is.Not.Null);
            Assert.That(Fields(copy), Is.EqualTo(Fields(Full())));
            Assert.That(copy.UpdateTime, Is.GreaterThan(0));
            Assert.That(copy.ApplyChanged(request), Is.Null);
        });
    }

    // The all-fields event (a login's first record) writes every field without a compare.
    [Test]
    public void AllEvent_WritesEveryField() {
        PlayerInfo copy = Blank();

        copy.Apply(new PlayerInfoUpdateEvent(Request(Full())));

        Assert.That(Fields(copy), Is.EqualTo(Fields(Full())));
    }

    // A record copied from another (a channel's cache from the World's answer) by field group.
    [Test]
    public void CopyByType_All() {
        PlayerInfo copy = Blank();
        PlayerInfo source = Full();
        source.UpdateTime = 99;

        copy.Update(UpdateField.All, source);
        source.ClubIds.Add(1);
        source.DungeonEnterLimits[5] = DungeonEnterLimit.MinLevel;

        Assert.Multiple(() => {
            Assert.That(copy.UpdateTime, Is.EqualTo(99));
            Assert.That(copy.ClubIds, Is.EqualTo(new long[] { 7, 8 }), "a copy, not the source's list");
            Assert.That(copy.DungeonEnterLimits.ContainsKey(5), Is.False, "a copy, not the source's table");
            Assert.That(copy.Name, Is.EqualTo("b"));
            Assert.That(copy.PlotExpiryTime, Is.EqualTo(15));
        });
    }

    [Test]
    public void CopyByType_OnlyTheGroup() {
        PlayerInfo copy = Blank();

        copy.Update(UpdateField.Level, Full());

        Assert.Multiple(() => {
            Assert.That(copy.Level, Is.EqualTo(30));
            Assert.That(copy.Name, Is.EqualTo("a"));
            Assert.That(copy.Job, Is.EqualTo(Job.Newbie));
        });
    }

    private static IEnumerable<TestCaseData> OneField() {
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Name = "x"), UpdateField.Profile).SetName("Name");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Picture = "x"), UpdateField.Profile).SetName("Picture");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Motto = "x"), UpdateField.Profile).SetName("Motto");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Gender = (int) Gender.Female), UpdateField.Profile).SetName("Gender");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.GuildId = 5), UpdateField.Profile).SetName("GuildId");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.GuildName = "x"), UpdateField.Profile).SetName("GuildName");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.AdminPermissions = 1), UpdateField.Profile).SetName("Admin");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Job = (int) Job.Knight), UpdateField.Job).SetName("Job");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Level = 2), UpdateField.Level).SetName("Level");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.GearScore = 2), UpdateField.GearScore).SetName("GearScore");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.RoomId = 2), UpdateField.Map).SetName("Room");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.DungeonId = 2), UpdateField.Map).SetName("Dungeon");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.LastOnlineTime = 2), UpdateField.Channel).SetName("LastOnline");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Health = new HealthUpdate { CurrentHp = 0, TotalHp = 5 }), UpdateField.Health).SetName("TotalHp");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.DeathState = (int) DeathState.FirstDeath), UpdateField.Death).SetName("Death");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Home = Home(mapId: 1)), UpdateField.Home).SetName("PlotMap");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Home = Home(plot: 1)), UpdateField.Home).SetName("Plot");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Home = Home(apartment: 1)), UpdateField.Home).SetName("Apartment");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Home = Home(expiry: 1)), UpdateField.Home).SetName("Expiry");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Home = Home(name: "x")), UpdateField.Home).SetName("HomeName");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Home = Home()), UpdateField.None).SetName("SameHome");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Trophy = new TrophyUpdate { Combat = 1 }), UpdateField.Trophy).SetName("Combat");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Trophy = new TrophyUpdate { Adventure = 1 }), UpdateField.Trophy).SetName("Adventure");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Trophy = new TrophyUpdate { Lifestyle = 1 }), UpdateField.Trophy).SetName("Lifestyle");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.Trophy = new TrophyUpdate()), UpdateField.None).SetName("SameTrophy");
        yield return new TestCaseData((Action<PlayerUpdateRequest>) (r => r.PremiumTime = 2), UpdateField.PremiumTime).SetName("Premium");
    }

    private static HomeUpdate Home(int mapId = 0, int plot = 0, int apartment = 0, long expiry = 0, string name = "Unknown") =>
        new() { MapId = mapId, PlotNumber = plot, ApartmentNumber = apartment, ExpiryTime = new Timestamp { Seconds = expiry }, Name = name };

    [TestCaseSource(nameof(OneField))]
    public void OneField_ItsOwnChange(Action<PlayerUpdateRequest> set, UpdateField expected) {
        var request = new PlayerUpdateRequest { AccountId = 1, CharacterId = 2 };
        set(request);

        Assert.That(new PlayerInfoUpdateEvent(Blank(), request).Type, Is.EqualTo(expected));
    }

    // The trophy and home groups are written whole.
    [Test]
    public void TrophyAndHome_Applied() {
        PlayerInfo copy = Blank();

        copy.ApplyChanged(new PlayerUpdateRequest {
            AccountId = 1, CharacterId = 2, Trophy = new TrophyUpdate { Combat = 1, Adventure = 2, Lifestyle = 3 }, Home = Home(5, 6, 7, 8, "h"),
        });

        Assert.Multiple(() => {
            Assert.That((copy.AchievementInfo.Combat, copy.AchievementInfo.Adventure, copy.AchievementInfo.Lifestyle), Is.EqualTo((1, 2, 3)));
            Assert.That((copy.PlotMapId, copy.PlotNumber, copy.ApartmentNumber, copy.PlotExpiryTime, copy.HomeName), Is.EqualTo((5, 6, 7, 8L, "h")));
        });
    }

    // A record that names another channel by now (the player logged in there) is left alone.
    [Test]
    public void ApplyIfOn_OtherChannel_Untouched() {
        PlayerInfo copy = Blank();
        copy.Channel = 3;

        Assert.Multiple(() => {
            Assert.That(copy.ApplyIfOn(2, new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, Level = 9 }), Is.Null);
            Assert.That(copy.ApplyIfOn(3, new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, Level = 1 }), Is.Null);
            Assert.That(copy.ApplyIfOn(3, new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, Level = 9 })?.Type, Is.EqualTo(UpdateField.Level));
            Assert.That(copy.Level, Is.EqualTo(9));
        });
    }
}
