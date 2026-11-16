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
using Microsoft.EntityFrameworkCore;
using static Maple2.Model.Error.MigrationError;

namespace Maple2.Server.Tests.Login;

// The answers of a character select (CharacterManagement 0) that does not enter: LoginToGame with the client's move
// error, whose text the client shows over the character list. A world that answered with something the session
// cannot use also ends the connection after the answer; a full or gone channel leaves the list open for another try.
public class CharacterSelectErrorTests {
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

    private void Select(long characterId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(characterId);
            writer.WriteShort(0);
        }));
    }

    private static string Error(Maple2.Model.Error.MigrationError error, string message) =>
        Convert.ToHexString(MigrationPacket.LoginToGameError(error, message).ToArray());

    private string[] SentHex() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    // Whether the session still answers: a second select of the account's character enters.
    private bool StillOpen() {
        test.World.MigrateOutError = null;
        test.World.AccountInfoError = null;
        test.World.MigrateOutAddress = "127.0.0.1";
        Select(test.CharacterId);
        return test.TakeSentOps().SequenceEqual(new[] { SendOp.LoginToGame });
    }

    // Another account's character, or one that is not there.
    [Test]
    public void UnknownCharacter_InvalidCharacter() {
        long other = test.AddCharacter("Other", accountId: test.AccountId + 100);

        Select(other);
        Select(other + 1000);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(Enumerable.Repeat(Error(s_move_err_default, "Invalid character"), 2)));
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(StillOpen(), Is.True);
        });
    }

    [Test]
    public void FullChannel_MemberLimitText_ListStaysOpen() {
        var full = new RpcException(new Status(StatusCode.ResourceExhausted, "Channel is full"));
        test.World.MigrateOutError = full;

        Select(test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] { Error(s_move_err_member_limit, full.Message) }));
            Assert.That(StillOpen(), Is.True);
        });
    }

    [Test]
    public void OtherWorldRefusal_DefaultText_ThenCut() {
        var refused = new RpcException(new Status(StatusCode.Internal, "Broken"));
        test.World.MigrateOutError = refused;

        Select(test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] { Error(s_move_err_default, refused.Message) }));
            Assert.That(StillOpen(), Is.False);
        });
    }

    [TestCase("")]
    [TestCase("not-an-address")]
    public void BadChannelAddress_DefaultText_ThenCut(string address) {
        test.World.MigrateOutAddress = address;

        Select(test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] { Error(s_move_err_default, "Invalid game server address") }));
            Assert.That(StillOpen(), Is.False);
        });
    }

    // A failure that is not a world refusal (here the account lookup): logged, the default text, then cut.
    [Test]
    public void Failure_DefaultText_ThenCut() {
        test.World.AccountInfoError = new InvalidOperationException("lookup failed");

        Select(test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] { Error(s_move_err_default, "lookup failed") }));
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(StillOpen(), Is.False);
        });
    }

    // The client writes modes 0 to 5 only; another is a packet it could not have written, dropped by the router.
    [TestCase((byte) 6)]
    [TestCase((byte) 0xFF)]
    public void UnknownMode_Throws(byte mode) {
        Assert.Throws<ArgumentException>(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteLong(test.CharacterId);
        })));
    }

    // A rename the world could not be told of is still stored and listed: the world learns it when the character enters.
    [Test]
    public void Rename_WorldDown_StoredAndListed() {
        long due = test.AddCharacter("Abcdefghijklm");
        test.World.UpdatePlayerError = new RpcException(new Status(StatusCode.Unavailable, "down"));

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(due);
            writer.WriteUnicodeString("Parry");
        }));

        Assert.Multiple(() => {
            Assert.That(test.Name(due), Is.EqualTo("Parry"));
            Assert.That(test.World.PlayerUpdates, Has.Count.EqualTo(1));
            Assert.That(test.TakeSentOps(), Does.Contain(SendOp.CharacterList));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
