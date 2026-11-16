using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 57 and 58 with a character that exists: the helper's account joins or leaves the home's list and
// ResponseCube 0x39 / 0x3a answers it with the account; the owner, a helper already listed and a character that is
// not a helper are refused with s_empty_string and change nothing.
public class RequestCubeBuildHelperTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private RequestCubeHandler handler = null!;
    private const long HelperAccount = 7_700_000_001;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterHome();
        handler = new RequestCubeHandler { TableMetadata = test.TableMetadata };
        session.Player.Value.Home.Indoor.Name = "Home";
        using (GameStorage.Request db = test.Storage.Context()) {
            var model = new Character { AccountId = HelperAccount, Name = "Helper", Job = Job.Knight, Mastery = new Mastery() };
            model.ReturnMaps.Push(2000062);
            long id = db.CreateCharacter(model)!.Id;
            test.World.AddPlayer(HelperAccount, id, "Helper");
        }
        test.TakeSent();
    }

    private void Send(byte command, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteUnicodeString(name);
        }));
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Add_JoinsTheHelperAndAnswersItsAccount() {
        Send(57, "Helper");

        Assert.That(Sent(), Does.Contain(Hex(CubePacket.AddBuildPermission(0, HelperAccount))));
        Assert.That(session.Housing.IsBuilder(HelperAccount), Is.True);
    }

    [Test]
    public void Add_IsStoredWithTheHome() {
        Send(57, "Helper");

        using GameStorage.Request db = test.Storage.Context();
        Home? stored = db.GetHome(session.AccountId);
        Assert.That(stored?.Builders, Does.Contain(HelperAccount));
    }

    [Test]
    public void Add_TwiceIsRefused() {
        Send(57, "Helper");
        test.TakeSent();

        Send(57, "Helper");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.AddBuildPermission((byte) UgcMapError.s_empty_string)) }));
        Assert.That(session.Player.Value.Home.Builders, Has.Count.EqualTo(1));
    }

    [Test]
    public void Add_TheOwnersOwnCharacterIsRefused() {
        Send(57, session.PlayerName);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.AddBuildPermission((byte) UgcMapError.s_empty_string)) }));
        Assert.That(session.Player.Value.Home.Builders, Is.Empty);
    }

    [Test]
    public void Remove_LeavesTheHelperAndAnswersItsAccount() {
        Send(57, "Helper");
        test.TakeSent();

        Send(58, "Helper");

        Assert.That(Sent(), Does.Contain(Hex(CubePacket.RemoveBuildPermission(0, HelperAccount))));
        Assert.That(session.Housing.IsBuilder(HelperAccount), Is.False);
    }

    [Test]
    public void Remove_OfACharacterThatIsNotAHelper_IsRefused() {
        Send(58, "Helper");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.RemoveBuildPermission((byte) UgcMapError.s_empty_string)) }));
    }
}
