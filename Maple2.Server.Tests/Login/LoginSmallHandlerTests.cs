using System;
using System.Linq;
using Grpc.Core;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using static Maple2.Model.Error.MigrationError;

namespace Maple2.Server.Tests.Login;

// The login server's small handlers, each fed the packet the client writes.
public class LoginSmallHandlerTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private bool Open() {
        session.Send(NoticePacket.Disconnect(new Maple2.Model.Game.InterfaceText("probe")));
        return test.TakeSent().Count == 1;
    }

    // The client's heartbeat answer: int server tick (the one it was asked with), int its own tick. The first pair is
    // kept; later pairs replace it. A zero tick is not a pair.
    [Test]
    public void Heartbeat_KeepsTheLatestTicks() {
        var handler = new ResponseHeartbeatHandler();
        void Answer(int server, int client) => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(server);
            writer.WriteInt(client);
        }));

        Answer(0, 5);
        Assert.That((session.ServerTick, session.ClientTick), Is.EqualTo((0, 0)));
        Answer(100, 5);
        Assert.That((session.ServerTick, session.ClientTick), Is.EqualTo((100, 5)));
        Answer(300, 9);
        Assert.Multiple(() => {
            Assert.That((session.ServerTick, session.ClientTick), Is.EqualTo((300, 9)));
            Assert.That(handler.OpCode, Is.EqualTo(RecvOp.ResponseHeartbeat));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    private static Maple2.PacketLib.Tools.IByteReader Hello(uint version, short region) => TestGameSession.Packet(writer => {
        writer.WriteInt(unchecked((int) version));
        writer.WriteShort(47);
        writer.WriteShort(region);
    });

    // The client's hello of this version is answered with the login request, from any region (another is logged).
    [TestCase((short) 0)]
    [TestCase((short) 2)]
    public void Version_Current_AskedToLogIn(short region) {
        var handler = new ResponseVersionHandler();

        handler.Handle(session, Hello(Session.VERSION, region));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(RequestPacket.Login().ToArray()) }));
            Assert.That(handler.OpCode, Is.EqualTo(RecvOp.ResponseVersion));
        });
    }

    // Another version is cut before the login request (its packets would not read as this server writes them).
    [Test]
    public void Version_Other_Cut() {
        new ResponseVersionHandler().Handle(session, Hello(Session.VERSION + 1, 0));

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.Empty.Or.EqualTo(new[] { SendOp.RequestLogin }));
            Assert.That(Open(), Is.False);
        });
    }

    // The move from a channel to the login server: a token the world does not take is answered with the default move
    // error, then the connection ends.
    [TestCase(true)]
    [TestCase(false)]
    public void ResponseKey_RefusedToken_DefaultErrorThenClosed(bool rpc) {
        var world = new RefusingWorld(rpc ? new RpcException(new Status(StatusCode.PermissionDenied, "token")) : new InvalidOperationException("token"));
        var owner = new TestLoginSession();
        LoginSession fresh = owner.Start(init: false);

        new ResponseKeyHandler { World = world }.Handle(fresh, TestGameSession.Packet(writer => {
            writer.WriteLong(test.AccountId);
            writer.WriteLong(1234);
            writer.WriteBytes(Guid.NewGuid().ToByteArray());
        }));

        Assert.Multiple(() => {
            Assert.That(owner.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(MigrationPacket.MoveResult(s_move_err_default).ToArray()) }));
            Assert.That(fresh.HasAccount, Is.False);
        });
        fresh.Dispose();
    }

    private sealed class RefusingWorld(Exception error) : Maple2.Server.World.Service.World.WorldClient {
        public override Maple2.Server.World.Service.MigrateInResponse MigrateIn(Maple2.Server.World.Service.MigrateInRequest request, CallOptions options) =>
            throw error;
    }

    // Quit (the client's exit button) closes in order.
    [Test]
    public void Quit_Closes() {
        var handler = new QuitHandler();

        handler.Handle(session, TestGameSession.Packet(_ => { }));

        Assert.Multiple(() => {
            Assert.That(handler.OpCode, Is.EqualTo(RecvOp.RequestQuit));
            Assert.That(Open(), Is.False);
        });
    }

    // An EventReward of a game dialog is read and dropped at the login; the burning levelup pick after the client
    // left the character list cuts.
    [Test]
    public void EventReward_OtherCommand_Dropped() {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(Open(), Is.True);
        });
    }

    [Test]
    public void AfterLeavingTheList_PickAndPicture_Cut() {
        session.LeaveCharacterList();
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0x10);
            writer.WriteLong(test.CharacterId);
        }));
        Assert.That(Open(), Is.False);

        var owner = new TestLoginSession();
        LoginSession second = owner.Start();
        owner.TakeSent();
        second.LeaveCharacterList();
        new UgcHandler { GameStorage = test.Storage }.Handle(second, TestGameSession.Packet(writer => {
            writer.WriteByte(11);
            writer.WriteUnicodeString($"data/profiles/avatar/{test.CharacterId}/a.png");
        }));
        second.Send(NoticePacket.Disconnect(new Maple2.Model.Game.InterfaceText("probe")));
        Assert.That(owner.TakeSent(), Is.Empty);
        second.Dispose();
    }
}
