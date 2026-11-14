using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class BillboardRulesTests {
    // RequestItemUse 0x0019 with a billboard item: after the item uid, one string title'description'flag.
    [TestCase("Shop'Come in'1", true, true, BillboardInstallResult.Success)]
    [TestCase("Shop'Come in", true, true, BillboardInstallResult.Fail)]
    [TestCase("Shop'Come in'1", false, true, BillboardInstallResult.Fail)]
    [TestCase("Shop'Come in'1", true, false, BillboardInstallResult.Fail)]
    public void InstallResult(string text, bool itemParameters, bool placed, BillboardInstallResult expected) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(text);
        var packet = new ByteReader(writer.ToArray(), 0);
        bool allowed = BillboardRules.TextAllowed(packet.ReadUnicodeString().Split("'"));
        Assert.That(BillboardRules.InstallResult(allowed, itemParameters, placed), Is.EqualTo(expected));
    }

    // InteractObject add: after the four strings the client reads f32 scale, bool, and for a billboard i64 board id, name.
    [TestCase("1.5", 1.5f)]
    [TestCase("2", 2f)]
    [TestCase(null, 1f)]
    [TestCase("", 1f)]
    [TestCase("big", 1f)]
    [TestCase("0", 1f)]
    public void ScaleOfInstalledBillboard(string? parameter, float expected) {
        var owner = new Character { Id = 77, Name = "Owner", Mastery = new Mastery() };
        var mesh = new Ms2InteractMesh(5, default, default);
        var billboard = new InteractBillBoardObject("BillBoard_x", mesh, owner) {
            Model = "MS2InteractMesh",
            Scale = BillboardRules.Scale(parameter),
            BoardUid = 4242,
        };
        var writer = new ByteWriter();
        billboard.WriteTo(writer);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadString(), Is.EqualTo("BillBoard_x"));
        packet.ReadByte();
        Assert.That(packet.ReadByte(), Is.EqualTo(8));
        Assert.That(packet.ReadInt(), Is.EqualTo(5));
        packet.Skip(24);
        Assert.That(packet.ReadUnicodeString(), Is.EqualTo("MS2InteractMesh"));
        packet.ReadUnicodeString();
        packet.ReadUnicodeString();
        packet.ReadUnicodeString();
        Assert.That(packet.ReadFloat(), Is.EqualTo(expected));
        Assert.That(packet.ReadBool(), Is.False);
        Assert.That(packet.ReadLong(), Is.EqualTo(4242));
        Assert.That(packet.ReadUnicodeString(), Is.EqualTo("Owner"));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // The answer the billboard dialog reads: u8 mode 7, i32 result.
    [TestCase(BillboardInstallResult.Success, (byte) 0)]
    [TestCase(BillboardInstallResult.Fail, (byte) 1)]
    [TestCase(BillboardInstallResult.MaxPerField, (byte) 3)]
    public void InstallAnswerBytes(BillboardInstallResult result, byte low) {
        byte[] bytes = PlayerHostPacket.BillboardInstallResult(result).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 1 + 4));
        Assert.That(bytes[2], Is.EqualTo(7));
        Assert.That(bytes[3], Is.EqualTo(low));
        Assert.That(bytes[4] | bytes[5] | bytes[6], Is.EqualTo(0));
    }

    // The window's last long is the board's id: the client compares it with the long of the board's interact
    // object (interactobject.cpp:1670), and the staff remove request (ItemBillboard 8) sends it back.
    [Test]
    public void BoardIdsAreNeverZeroAndDiffer() {
        long first = BillboardRules.NextBoardUid();
        long second = BillboardRules.NextBoardUid();
        Assert.That(first, Is.Not.EqualTo(0));
        Assert.That(second, Is.GreaterThan(first));
    }

    [Test]
    public void TheWindowEndsWithTheBoardId() {
        var board = new InteractBillBoardObject("BillBoard_x", new Ms2InteractMesh(5, default, default),
            new Character { Id = 77, Name = "Owner", Mastery = new Mastery() }) {
            BoardUid = 0x1122334455667788,
        };
        byte[] bytes = PlayerHostPacket.AdBalloonWindow(board).ToArray();
        Assert.That(bytes[2], Is.EqualTo(6));
        Assert.That(System.BitConverter.ToInt64(bytes, bytes.Length - 8), Is.EqualTo(0x1122334455667788));
    }

    // The long before the name in the interact object is the same id, not the owner's.
    [Test]
    public void TheInteractObjectCarriesTheWindowsBoardId() {
        var board = new InteractBillBoardObject("BillBoard_x", new Ms2InteractMesh(5, default, default),
            new Character { Id = 77, Name = "Owner", Mastery = new Mastery() }) {
            BoardUid = 0x1122334455667788,
        };
        var writer = new ByteWriter();
        board.WriteTo(writer);
        byte[] bytes = writer.ToArray();
        byte[] window = PlayerHostPacket.AdBalloonWindow(board).ToArray();
        int nameSize = 2 + 2 * "Owner".Length;
        long inObject = System.BitConverter.ToInt64(bytes, bytes.Length - nameSize - 8);
        Assert.That(inObject, Is.EqualTo(System.BitConverter.ToInt64(window, window.Length - 8)));
        Assert.That(inObject, Is.Not.EqualTo(77));
    }
}
