using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A player who made a room alone (RoomDungeon 2 with the party flag off) while in a party of their own (an invite that
// was declined leaves the inviter alone in it) arrives in the room's lobby. The party loads after the arrival and
// carries no room. The lobby's portal to the dungeon (portal type 9) then sends RoomDungeon 9 and must enter the
// room's first field. Before, the party load cleared the session's room and the server logged "Field is null".
public class DungeonSoloRoomPartyLoadTests {
    private const int Room = 21009001;
    private const int Lobby = 50000001;
    private const int FirstField = 2000292;

    private TestGameSession test = null!;

    private void Start() {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, Lobby) with { FieldIds = [FirstField] };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        test.EnterDungeon(room);
        test.AddRoomField(FirstField);
        test.World.AddPlayer(test.AccountId, test.CharacterId, "solo");
        // The party the login loads (World PartyInfo): this player alone, no room set.
        test.Session.Party = new PartyManager(test.World, test.Session);
        test.Session.Party.SetParty(new PartyInfo {
            Id = 3,
            LeaderAccountId = test.AccountId,
            LeaderCharacterId = test.CharacterId,
            LeaderName = "solo",
            Members = { new PartyInfo.Types.Member { CharacterId = test.CharacterId } },
        });
        test.TakeSent();
    }

    [Test]
    public void ThePartyLoadKeepsTheRoomTheSessionStandsIn() {
        Start();
        Assert.That(test.Session.Dungeon.Metadata?.Id, Is.EqualTo(Room));
    }

    [Test]
    public void TheLobbyPortalEntersTheFirstField() {
        Start();
        // RoomDungeon 9 as the live client sent it; the client writes the mode byte alone
        // (PortalAction::SendRoomDungeon), the rest is the send buffer's leftover.
        // The move to the first field starts (State ChangeMap, then LeaveField); the test session lacks what
        // LeaveField clears, so the move stops there. Before, the handler logged "Field is null" and the player stayed.
        try {
            new DungeonRoomHandler { TableMetadata = test.TableMetadata, World = test.World }.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteBytes(
                [0x09, 0x04, 0x00, 0x00, 0x00, 0x00, 0x1D, 0x2C, 0x04, 0x69, 0x74, 0x00, 0x00, 0x00])));
        } catch (ArgumentNullException) { }
        Assert.That(test.Session.State, Is.EqualTo(SessionState.ChangeMap));
    }
}
