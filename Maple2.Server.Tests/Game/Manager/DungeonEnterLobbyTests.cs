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

// The party panel's Enter Dungeon (DungeonManager.EnterLobby): the world is asked whether the room still stands before
// the move. A room that is gone clears the party's dungeon only when the party still names that room; a member holding
// an older room id takes over the party's current room instead. The room and the party are made up.
public class DungeonEnterLobbyTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Room = 23050501;
    private const int PartyRoomId = 55;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly List<PartyRequest> partyAsked = [];

    private void Start(MigrationError check = MigrationError.ok) {
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
        test.World.FieldWorld = _ => new FieldResponse { Error = (int) check };
        test.TakeSent();
    }

    private Party Join() {
        var member = new PartyMember {
            PartyId = 5,
            Info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "member", "", "", Gender.Male, Job.Newbie, 20),
                "", new AchievementInfo(), []) { Channel = 1 },
        };
        var party = new Party(5, member) { DungeonId = Room, DungeonLobbyRoomId = PartyRoomId, DungeonSet = true };
        party.Members[member.CharacterId] = member;
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(session.Party, party);
        return party;
    }

    private void HoldRoom(int roomId) =>
        typeof(DungeonManager).GetProperty("LobbyRoomId", Any)!.SetValue(session.Dungeon, roomId);

    private SendOp[] Ops() => test.TakeSent().Select(TestGameSession.Op).ToArray();

    [Test]
    public void WithoutAParty_TheNotFoundPartyError() {
        Start();
        session.Dungeon.EnterLobby();
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.GameToGame }));
    }

    [Test]
    public void AnotherCheckError_IsSentAndTheDungeonIsKept() {
        Start(MigrationError.s_move_err_no_server);
        Join();
        session.Dungeon.EnterLobby();
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.GameToGame }));
        Assert.That(partyAsked, Is.Empty);
    }

    [Test]
    public void ARoomGoneThatThePartyNoLongerNames_TakesOverThePartysRoom() {
        Start(MigrationError.s_move_err_dungeon_not_exist_2);
        Join();
        HoldRoom(99);

        session.Dungeon.EnterLobby();

        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.GameToGame }));
        Assert.That(partyAsked, Is.Empty);
        Assert.That(session.Dungeon.Metadata?.Id, Is.EqualTo(Room));
    }

    [Test]
    public void ARoomGoneThatThePartyStillNames_ClearsThePartysDungeon() {
        Start(MigrationError.s_move_err_dungeon_not_exist_2);
        Join();
        HoldRoom(PartyRoomId);

        session.Dungeon.EnterLobby();

        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.GameToGame }));
        PartyRequest cleared = partyAsked.Single();
        Assert.That((cleared.SetDungeon.PartyId, cleared.SetDungeon.DungeonId, cleared.SetDungeon.Set), Is.EqualTo((5, 0, false)));
        Assert.That(session.Dungeon.Metadata, Is.Null);
    }
}
