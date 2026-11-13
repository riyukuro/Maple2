using System;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using static Maple2.Model.Error.MigrationError;

namespace Maple2.Server.Tests.Login;

// The character select (CharacterManagement 0): the client writes the character and the channel picked on the
// server list, 0 when none was picked (UIServerListDialog::EnterGame, WriteLoginPing). A picked channel is the
// one the character enters, or the client's no server text when that channel is gone.
public class CharacterSelectChannelTests {
    private TestLoginSession test = null!;
    private CharacterManagementHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        handler = new CharacterManagementHandler {
            World = test.World, GameStorage = test.Storage, BanWordStorage = new BanWordStorage(words),
            ItemMetadata = null!, TableMetadata = test.Rows.TableMetadata, ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        session = test.Start();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void Select(short channel) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(test.CharacterId);
            writer.WriteShort(channel);
        }));
    }

    [Test]
    public void APickedChannelIsAskedForExactly() {
        Select(3);

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LoginToGame }));
            Assert.That(test.World.Moves.Select(move => (move.CharacterId, move.HasChannel, move.Channel, move.ExactChannel)),
                Is.EqualTo(new[] { (test.CharacterId, true, 3, true) }));
        });
    }

    [Test]
    public void NoPickedChannelLeavesItToTheWorld() {
        Select(0);

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LoginToGame }));
            Assert.That(test.World.Moves.Select(move => (move.HasChannel, move.ExactChannel)), Is.EqualTo(new[] { (false, false) }));
        });
    }

    // The world refuses a picked channel that is gone: the client's no server text, and the list stays open.
    [Test]
    public void AGoneChannelGetsTheNoServerText() {
        var gone = new RpcException(new Status(StatusCode.Unavailable, "Channel 3 is not available"));
        test.World.MigrateOutError = gone;

        Select(3);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(MigrationPacket.LoginToGameError(s_move_err_no_server, gone.Message).ToArray()),
        }));
        // Still open: a second pick is answered.
        test.World.MigrateOutError = null;
        Select(1);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LoginToGame }));
    }

    // WorldService.PickGameChannel: the World has channels 1, 3 and 4; 1 and 3 are active (2 is a gap in the ids,
    // 5 is gone), 4 is inactive; 1 is the first.
    [TestCase(null, false, 1)]
    [TestCase(null, true, 1)]
    [TestCase(3, false, 3)]
    [TestCase(3, true, 3)]
    [TestCase(4, false, 1)]
    [TestCase(4, true, -1)]
    public void APickedInactiveChannelIsRefused(int? requested, bool exact, int expected) {
        Assert.That(Pick(requested, exact), Is.EqualTo(expected));
    }

    // The client sends its saved channel clamped to the channel count: a gap or a gone channel enters the first one.
    [TestCase(2)]
    [TestCase(5)]
    public void APickedChannelTheWorldLacksEntersTheFirst(int requested) {
        Assert.Multiple(() => {
            Assert.That(Pick(requested, exact: true), Is.EqualTo(1));
            Assert.That(Pick(requested, exact: false), Is.EqualTo(1));
        });
    }

    private static int Pick(int? requested, bool exact) =>
        WorldService.PickGameChannel(requested, exact, id => id is 1 or 3, id => id is 1 or 3 or 4, () => 1);

    [Test]
    public void NoActiveChannelIsNone() {
        Assert.That(WorldService.PickGameChannel(null, false, _ => false, _ => false, () => -1), Is.EqualTo(-1));
    }
}
