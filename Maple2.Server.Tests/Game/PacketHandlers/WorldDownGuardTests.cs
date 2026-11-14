using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Game -> World calls with the World down: each refuses with the client's own error, or at logout only logs, and
// none throws out of the handler or the manager.
public class WorldDownGuardTests {
    private const int PartyId = 3;
    private const int ChatId = 9;
    private const int Room = 21009001;

    private TestGameSession test = null!;

    private static RpcException Down() => new(new Status(StatusCode.Unavailable, "down"));

    [SetUp]
    public void Start() {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000001) with { HelperRequireClearCount = 1 };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        test.World.AddPlayer(test.AccountId, test.CharacterId, "lone");
        test.Session.Party = new PartyManager(test.World, test.Session);
        test.TakeSent();
    }

    private void JoinParty() {
        test.Session.Party.SetParty(new PartyInfo {
            Id = PartyId,
            LeaderAccountId = test.AccountId,
            LeaderCharacterId = test.CharacterId,
            LeaderName = "lone",
            Members = { new PartyInfo.Types.Member { CharacterId = test.CharacterId } },
        });
        test.TakeSent();
    }

    // The party's disband at logout runs before the session is saved: a throw there skipped the save.
    [Test]
    public void PartyCheckDisband_WorldDown_OnlyLogs() {
        JoinParty();
        test.World.PartyWorld = _ => throw Down();

        Assert.DoesNotThrow(() => test.Session.Party.CheckDisband());
    }

    // A member who logs out during a ready check answers no on the way out; with the World down that is only logged.
    [Test]
    public void PartyDispose_DuringAVote_WorldDown_OnlyLogs() {
        JoinParty();
        test.Session.Party.StartReadyCheck(test.CharacterId + 500, 1);
        Assert.That(test.Session.Party.Party!.Vote, Is.Not.Null);
        bool replyAsked = false;
        test.World.PartyWorld = request => {
            replyAsked = request.PartyCase == PartyRequest.PartyOneofCase.VoteReply;
            throw Down();
        };

        Assert.DoesNotThrow(() => test.Session.Party.Dispose());
        Assert.That(replyAsked, Is.True);
    }

    [Test]
    public void GroupChatCheckDisband_WorldDown_OnlyLogs() {
        var manager = new GroupChatManager(new GroupChatInfo { Id = ChatId }, test.Session);
        test.World.GroupChatWorld = _ => throw Down();

        Assert.DoesNotThrow(() => manager.CheckDisband());
    }

    // RoomDungeon 15 (find helpers) for a room with a helperRequireClearCount: the party's not-found answer.
    [TestCase((byte) 15)]
    [TestCase((byte) 16)]
    public void DungeonRecruit_WorldDown_AnswersNotFound(byte mode) {
        test.World.PartyWorld = _ => throw Down();
        var handler = new DungeonRoomHandler { TableMetadata = test.TableMetadata, World = test.World };

        Assert.DoesNotThrow(() => handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteInt(Room);
        })));

        byte[][] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Party).ToArray();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(PartyPacket.Error(PartyError.s_party_err_not_found).ToArray()),
        }));
    }
}
