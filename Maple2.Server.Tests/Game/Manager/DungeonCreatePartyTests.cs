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

// A dungeon room request of a party (DungeonManager.CreateDungeonRoom with withParty): only the leader asks, each
// member's enter limit and the room's size decide, and the room the world makes is registered on the party. The room
// and the party are made up.
public class DungeonCreatePartyTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Room = 23050401;
    private const int RoomId = 777;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly List<PartyRequest> partyAsked = [];

    private void Start(int maxUsers = 4) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000016);
        room = room with { Limit = room.Limit with { MaxUserCount = maxUsers } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        session.Party = new PartyManager(test.World, session);
        test.World.PartyWorld = request => {
            partyAsked.Add(request);
            return new PartyResponse();
        };
        test.World.FieldWorld = _ => new FieldResponse { RoomId = RoomId };
        test.TakeSent();
        test.World.FieldRequests.Clear();
    }

    private Party Join(int members, bool leaderIsSelf) {
        PartyMember Member(long characterId) => new() {
            PartyId = 5,
            Info = new PlayerInfo(new CharacterInfo(characterId + 1000, characterId, $"member{characterId}", "", "", Gender.Male, Job.Newbie, 20),
                "", new AchievementInfo(), []) { Channel = 1 },
        };
        PartyMember me = Member(session.CharacterId);
        PartyMember leader = leaderIsSelf ? me : Member(session.CharacterId + 1);
        var party = new Party(5, leader);
        party.Members[me.CharacterId] = me;
        party.Members[leader.CharacterId] = leader;
        for (int i = 2; party.Members.Count < members; i++) {
            PartyMember extra = Member(session.CharacterId + i);
            party.Members[extra.CharacterId] = extra;
        }
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(session.Party, party);
        return party;
    }

    private SendOp[] Ops() => test.TakeSent().Select(TestGameSession.Op).ToArray();

    [Test]
    public void WithAPartyButNoParty_TheInvalidPartyError() {
        Start();
        session.Dungeon.CreateDungeonRoom(Room, withParty: true);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
        Assert.That(test.World.FieldRequests, Is.Empty);
    }

    [Test]
    public void AMemberWhoIsNotTheLeader_TheNotChiefError() {
        Start();
        Join(2, leaderIsSelf: false);
        session.Dungeon.CreateDungeonRoom(Room, withParty: true);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
        Assert.That(test.World.FieldRequests, Is.Empty);
    }

    [Test]
    public void AMemberBlockedByTheEnterLimit_TheEntranceError() {
        Start();
        Party party = Join(2, leaderIsSelf: true);
        party.Members.Values.Last(member => member.CharacterId != session.CharacterId)
            .Info.DungeonEnterLimits[Room] = DungeonEnterLimit.MinLevel;
        session.Dungeon.CreateDungeonRoom(Room, withParty: true);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.FieldEntrance }));
        Assert.That(test.World.FieldRequests, Is.Empty);
    }

    [Test]
    public void APartyOverTheRoomsMaximum_TheOverMaxError() {
        Start(maxUsers: 2);
        Join(3, leaderIsSelf: true);
        session.Dungeon.CreateDungeonRoom(Room, withParty: true);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
        Assert.That(test.World.FieldRequests, Is.Empty);
    }

    [Test]
    public void APartyInADungeonMatch_ThePartyError() {
        Start();
        Join(2, leaderIsSelf: true).DungeonMatchKind = 1;
        session.Dungeon.CreateDungeonRoom(Room, withParty: true);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.Party }));
        Assert.That(test.World.FieldRequests, Is.Empty);
    }

    [Test]
    public void ThePartysRoom_IsMadeForItsSizeAndRegisteredOnTheParty() {
        Start();
        Join(3, leaderIsSelf: true);

        session.Dungeon.CreateDungeonRoom(Room, withParty: true);

        FieldRequest create = test.World.FieldRequests.Single(request => request.CreateDungeon != null);
        Assert.That((create.CreateDungeon.DungeonId, create.CreateDungeon.Size, create.CreateDungeon.PartyId), Is.EqualTo((Room, 3, 5)));
        PartyRequest set = partyAsked.First(request => request.SetDungeon != null);
        Assert.That((set.SetDungeon.PartyId, set.SetDungeon.DungeonId, set.SetDungeon.DungeonRoomId, set.SetDungeon.Set),
            Is.EqualTo((5, Room, RoomId, true)));
    }

    [Test]
    public void AWorldThatRefusesTheRoom_HasTheRoomDestroyedAndTheErrorSent() {
        Start();
        Join(2, leaderIsSelf: true);
        test.World.PartyWorld = request => {
            partyAsked.Add(request);
            return new PartyResponse { Error = (int) PartyError.s_party_err_dungeon_match_another };
        };

        session.Dungeon.CreateDungeonRoom(Room, withParty: true);

        Assert.That(test.World.FieldRequests.Select(request => (request.CreateDungeon != null, request.DestroyDungeon != null)),
            Is.EqualTo(new[] { (true, false), (false, true) }));
        Assert.That(test.World.FieldRequests[1].DestroyDungeon.RoomId, Is.EqualTo(RoomId));
        Assert.That(Ops(), Does.Contain(SendOp.Party));
    }

    [Test]
    public void ARoomTheWorldCannotMake_SendsTheMigrationErrorAndRegistersNothing() {
        Start();
        Join(2, leaderIsSelf: true);
        test.World.FieldWorld = _ => new FieldResponse { Error = (int) MigrationError.s_move_err_no_server };

        session.Dungeon.CreateDungeonRoom(Room, withParty: true);

        Assert.That(partyAsked, Is.Empty);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.GameToGame }));
    }
}
