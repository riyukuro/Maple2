using System;
using System.Linq;
using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube's build budget (56) and build helper list (57, 58): the owner puts mesos and merats of its wallet into
// the home's budget, and answers are ResponseCube 0x38, 0x39 and 0x3a with an error byte, no more fields on an error.
public class RequestCubeBuildBudgetTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private RequestCubeHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterHome();
        handler = new RequestCubeHandler { TableMetadata = test.TableMetadata };
        session.Player.Value.Home.Indoor.Name = "Home"; // a home that is set up
        session.Currency.Meso = 10000;
        test.TakeSent();
    }

    private void Send(byte command, Action<ByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private void Budget(long meso, long merat) => Send(56, writer => { writer.WriteLong(meso); writer.WriteLong(merat); });

    [Test]
    public void SetBuildBudget_Raise_TakenFromTheWallet() {
        Budget(4000, 0);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(4000));
            Assert.That(session.Currency.Meso, Is.EqualTo(6000));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.UpdateBuildBudget(0, 4000, 0))));
        });
    }

    [Test]
    public void SetBuildBudget_Fall_GivenBack() {
        Budget(4000, 0);
        test.TakeSent();

        Budget(1000, 0);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(1000));
            Assert.That(session.Currency.Meso, Is.EqualTo(9000));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.UpdateBuildBudget(0, 1000, 0))));
        });
    }

    [Test]
    public void SetBuildBudget_MoreThanTheWallet_RefusedWithTheBalanceError() {
        Budget(10001, 0);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.UpdateBuildBudget((byte) UgcMapError.s_err_ugcmap_not_enough_meso_balance)) }));
        });
    }

    [Test]
    public void SetBuildBudget_MoreMeratThanTheWallet_RefusedWithTheBalanceError() {
        Budget(0, 1);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.UpdateBuildBudget((byte) UgcMapError.s_err_ugcmap_not_enough_merat_balance)) }));
    }

    [Test]
    public void SetBuildBudget_Negative_Refused() {
        Budget(-1, 0);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.UpdateBuildBudget((byte) UgcMapError.s_empty_string)) }));
        });
    }

    [Test]
    public void SetBuildBudget_HomeNotSetUp_AnswersNoHome() {
        TestGameSession bare = new();
        bare.Start();
        bare.EnterHome();
        var bareHandler = new RequestCubeHandler { TableMetadata = bare.TableMetadata };
        bare.TakeSent();

        bareHandler.Handle(bare.Session, TestGameSession.Packet(writer => { writer.WriteByte(56); writer.WriteLong(1000); writer.WriteLong(0); }));

        Assert.That(bare.TakeSent().Select(Convert.ToHexString),
            Does.Contain(Hex(CubePacket.UpdateBuildBudget((byte) UgcMapError.s_ugcmap_no_home))));
    }

    [Test]
    public void AddBuildPermission_UnknownName_Refused() {
        Send(57, writer => writer.WriteUnicodeString("NoSuchCharacter"));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.AddBuildPermission((byte) UgcMapError.s_empty_string)) }));
    }

    [Test]
    public void AddBuildPermission_FeatureOff_Silent() {
        test.SetFeaturesOff("UGCMapDelegator");

        Send(57, writer => writer.WriteUnicodeString("NoSuchCharacter"));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void RemoveBuildPermission_NotAHelper_Refused() {
        Send(58, writer => writer.WriteUnicodeString("NoSuchCharacter"));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.RemoveBuildPermission((byte) UgcMapError.s_empty_string)) }));
    }
}
