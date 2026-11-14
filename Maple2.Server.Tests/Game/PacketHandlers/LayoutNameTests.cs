using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The names the client caps before it sends them: a layout or blueprint save (RequestCube 46, 64) holds at most
// 0x14 characters (uihomesaveslotdialog.cpp:452, :485), a blueprint upload (Ugc 1, kind LayoutBlueprint) at most
// UgcNameLengthMax (uiitemblueprintcreatedialog.cpp:5272). A longer name was stored as it came.
public class LayoutNameTests {
    private TestGameSession test = null!;

    private GameSession StartInHome() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        test.TakeSent();
        return session;
    }

    private void Save(GameSession session, byte command, string name) =>
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(2);
            writer.WriteUnicodeString(name);
        }));

    private static List<HomeLayout> Slots(GameSession session, byte command) =>
        command == 46 ? session.Player.Value.Home.Layouts : session.Player.Value.Home.Blueprints;

    [TestCase((byte) 46)]
    [TestCase((byte) 64)]
    public void LongerThanTheBox_IsNotSaved(byte command) {
        GameSession session = StartInHome();
        Save(session, command, new string('a', RequestCubeHandler.LayoutNameMax + 1));
        Save(session, command, "tab\there");
        Assert.Multiple(() => {
            Assert.That(Slots(session, command), Is.Empty);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [TestCase((byte) 46)]
    [TestCase((byte) 64)]
    public void TheBoxsLength_IsSaved(byte command) {
        GameSession session = StartInHome();
        string name = new string('a', RequestCubeHandler.LayoutNameMax);
        Save(session, command, name);
        Assert.That(Slots(session, command).Select(layout => layout.Name), Is.EqualTo(new[] { name }));
    }

    private const int Blueprint = 35200000;

    private (GameSession Session, Item Staged) StageBlueprint() {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { UgcNameLengthMax = 14 });
        test.Register(TestGameSession.Metadata(Blueprint));
        GameSession session = test.Start();
        test.ItemMetadata.TryGet(Blueprint, out ItemMetadata? metadata);
        var staged = new Item(metadata!) { Uid = 7001 };
        session.StagedUgcItem = staged;
        test.TakeSent();
        return (session, staged);
    }

    private void Upload(GameSession session, string name) {
        var handler = new UgcHandler {
            WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                NullLogger<WebStorage>.Instance),
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            World = null!,
        };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(0);
            writer.WriteByte((byte) UgcType.LayoutBlueprint);
            writer.WriteShort(0);
            writer.WriteInt(0);
            writer.WriteLong(session.AccountId);
            writer.WriteLong(session.CharacterId);
            writer.WriteUnicodeString("");
            writer.WriteUnicodeString("");
            writer.WriteLong(0);
            writer.WriteUnicodeString("");
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteUnicodeString("");
            writer.WriteLong(0);
            writer.WriteLong(7001);
            writer.WriteInt(Blueprint);
            writer.WriteUnicodeString(name);
        }));
    }

    [Test]
    public void BlueprintUpload_LongerThanTheField_IsRefused() {
        (GameSession session, Item staged) = StageBlueprint();
        Upload(session, new string('a', 15));
        Assert.Multiple(() => {
            Assert.That(staged.Template, Is.Null);
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(UgcPacket.Error(UgcType.LayoutBlueprint, UgcError.Refused).ToArray()) }));
        });
    }

    [Test]
    public void BlueprintUpload_OfTheFieldsLength_IsTaken() {
        (GameSession session, Item staged) = StageBlueprint();
        Upload(session, new string('a', 14));
        Assert.That(staged.Template?.Name, Is.EqualTo(new string('a', 14)));
    }
}
