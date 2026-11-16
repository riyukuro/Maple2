using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.Manager;

// The leader's reset of the party's dungeon (RoomDungeon 1, DungeonManager.Reset): the party's room is destroyed and
// the party's dungeon cleared. A room that is already gone clears it too; other errors are sent and keep it.
public class DungeonResetTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Room = 23050501;
    private const int PartyRoomId = 55;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly List<PartyRequest> partyAsked = [];
    private readonly List<FieldRequest> fieldAsked = [];

    private void Start(MigrationError destroy) {
        partyAsked.Clear();
        fieldAsked.Clear();
        test = new TestGameSession();
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [Room] = TestGameSession.DungeonRoom(Room, 50000016),
        }));
        test.Start();
        session.Party = new PartyManager(test.World, session);
        test.World.PartyWorld = request => {
            partyAsked.Add(request);
            return new PartyResponse();
        };
        test.World.FieldWorld = request => {
            fieldAsked.Add(request);
            return new FieldResponse { Error = (int) destroy };
        };
        Lead();
        test.TakeSent();
    }

    private void Lead() {
        var member = new PartyMember {
            PartyId = 5,
            Info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "leader", "", "", Gender.Male, Job.Newbie, 20),
                "", new AchievementInfo(), []) { Channel = 1 },
        };
        var party = new Party(5, member) { DungeonId = Room, DungeonLobbyRoomId = PartyRoomId, DungeonSet = true };
        party.Members[member.CharacterId] = member;
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(session.Party, party);
    }

    private void HoldRoom(int roomId) =>
        typeof(DungeonManager).GetProperty("LobbyRoomId", Any)!.SetValue(session.Dungeon, roomId);

    private SendOp[] Ops() => test.TakeSent().Select(TestGameSession.Op).ToArray();

    private void AssertCleared() {
        PartyRequest cleared = partyAsked.Single();
        Assert.That((cleared.SetDungeon.PartyId, cleared.SetDungeon.DungeonId, cleared.SetDungeon.DungeonRoomId, cleared.SetDungeon.Set),
            Is.EqualTo((5, 0, 0, false)));
    }

    [Test]
    public void ARoomDestroyed_ClearsThePartysDungeon() {
        Start(MigrationError.ok);
        HoldRoom(PartyRoomId);

        session.Dungeon.Reset();

        Assert.That(fieldAsked.Single().DestroyDungeon.RoomId, Is.EqualTo(PartyRoomId));
        Assert.That(Ops(), Is.Empty);
        AssertCleared();
    }

    [Test]
    public void ARoomAlreadyGone_ClearsThePartysDungeonWithNoError() {
        Start(MigrationError.s_move_err_dungeon_not_exist);
        HoldRoom(PartyRoomId);

        session.Dungeon.Reset();

        Assert.That(Ops(), Is.Empty);
        AssertCleared();
    }

    [Test]
    public void ALeaderHoldingAnOlderRoomId_ResetsThePartysRoom() {
        Start(MigrationError.ok);
        HoldRoom(0);

        session.Dungeon.Reset();

        Assert.That(fieldAsked.Single().DestroyDungeon.RoomId, Is.EqualTo(PartyRoomId));
        AssertCleared();
    }

    [Test]
    public void PlayersStillInside_TheErrorIsSentAndTheDungeonKept() {
        Start(MigrationError.s_move_err_InsideDungeonUser);
        HoldRoom(PartyRoomId);

        session.Dungeon.Reset();

        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.GameToGame }));
        Assert.That(partyAsked, Is.Empty);
    }
}
