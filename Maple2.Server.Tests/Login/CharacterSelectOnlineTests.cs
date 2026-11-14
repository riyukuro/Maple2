using System;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Server.Core.Constants;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// A character select of an account that is in game: another client of the account entered with a character it
// picked after this login was let in, so both would play. That game session is ended first (and saved by its end),
// as a login of an account in game ends it. Before, the select went on and the account had two game sessions.
public class CharacterSelectOnlineTests {
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

    private void Select() {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(test.CharacterId);
            writer.WriteShort(0);
        }));
    }

    [Test]
    public void TheAccountInGame_IsEndedBeforeTheMove() {
        long other = test.AddCharacter("Other");
        test.World.Account = new PlayerInfoResponse { AccountId = test.AccountId, CharacterId = other, Channel = 2 };

        Select();

        Assert.Multiple(() => {
            Assert.That(test.World.Disconnects.Select(request => (request.CharacterId, request.Force)), Is.EqualTo(new[] { (other, true) }));
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LoginToGame }));
            Assert.That(test.World.Moves.Select(move => move.CharacterId), Is.EqualTo(new[] { test.CharacterId }));
        });
    }

    // The select's token ends the account's earlier ones (a token of another client of the account left unused).
    [Test]
    public void TheSelectsToken_EndsEarlierOnes() {
        Select();

        Assert.That(test.World.Moves.Select(move => move.EndsEarlier), Is.EqualTo(new[] { true }));
    }

    [Test]
    public void AnAccountNotInGame_EndsNothing() {
        Select();

        Assert.Multiple(() => {
            Assert.That(test.World.Disconnects, Is.Empty);
            Assert.That(test.World.Moves.Select(move => move.CharacterId), Is.EqualTo(new[] { test.CharacterId }));
        });
    }

    [Test]
    public void ARecordThatWentOfflineMeanwhile_StillMoves() {
        test.World.Account = new PlayerInfoResponse { AccountId = test.AccountId, CharacterId = test.CharacterId, Channel = 1 };
        test.World.DisconnectError = new RpcException(new Status(StatusCode.NotFound, "offline"));

        Select();

        Assert.Multiple(() => {
            Assert.That(test.World.Disconnects, Has.Count.EqualTo(1));
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LoginToGame }));
        });
    }
}
