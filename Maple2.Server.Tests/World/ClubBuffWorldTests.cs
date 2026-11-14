using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Club = Maple2.Model.Game.Club.Club;

namespace Maple2.Server.Tests.World;

// The club buff on the World server (Club 13): only the leader at the time of the write, not on a disbanded club,
// and the buff is stored before the members are told.
public class ClubBuffWorldTests {
    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    private TestGameSession test = null!;
    private Players players = null!;
    private ClubManager manager = null!;
    private long[] ids = null!;

    private static PlayerInfo Info(long characterId, string name) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        ids = [test.CharacterId, test.CharacterId + 500];
        var infos = new Dictionary<long, PlayerInfo> {
            [ids[0]] = Info(ids[0], "Leader"),
            [ids[1]] = Info(ids[1], "Second"),
        };
        players = new Players(infos);
        Club club;
        using (GameStorage.Request db = test.Storage.Context()) {
            club = db.CreateClub(players, "Club", ids[0], infos.Values.ToList())!;
        }
        Assume.That(club, Is.Not.Null);
        manager = new ClubManager(club) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
        };
    }

    [TearDown]
    public void TearDown() {
        manager.Dispose();
    }

    // The row keeps the effect id only; the level comes from table/clubbuff.xml at load.
    private int StoredBuff() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetClub(players, manager.Club.Id)!.BuffId;
    }

    [Test]
    public void Leader_IsStored() {
        Assert.That(manager.SetBuff(ids[0], 110002001, 1), Is.EqualTo(ClubError.none));
        Assert.That((manager.Club.BuffId, manager.Club.BuffLevel), Is.EqualTo((110002001, 1)));
        Assert.That(StoredBuff(), Is.EqualTo(110002001));
    }

    [Test]
    public void NotTheLeader_Refused() {
        Assert.That(manager.SetBuff(ids[1], 110002001, 1), Is.EqualTo(ClubError.s_club_err_no_master));
        Assert.That(manager.Club.BuffId, Is.EqualTo(0));
        Assert.That(StoredBuff(), Is.EqualTo(0));
    }

    [Test]
    public void Disbanded_Refused() {
        manager.Disband();
        Assert.That(manager.SetBuff(ids[0], 110002001, 1), Is.EqualTo(ClubError.s_club_err_null_club));
        Assert.That(manager.Club.BuffId, Is.EqualTo(0));
    }

    // A save that throws (here a member without a player) undoes the buff as a refused one does, so the next save
    // of the row does not write a buff the leader was told failed.
    [Test]
    public void SaveThrows_BuffUndone() {
        manager.Club.Members[-1] = new Maple2.Model.Game.Club.ClubMember { Info = null! };
        Assert.Throws<NullReferenceException>(() => manager.SetBuff(ids[0], 110002001, 1));
        Assert.That((manager.Club.BuffId, manager.Club.BuffLevel), Is.EqualTo((0, 0)));
        manager.Club.Members.TryRemove(-1, out _);
    }
}
