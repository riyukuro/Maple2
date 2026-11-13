using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A refused design upload answers with Ugc 0, which unlocks the client's upload. The item id is made up.
public class UgcUploadErrorTests {
    private const int Design = 11300902;
    private const int Town = 90000100;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    // The client's upload lock (CUgcManager::LockForUpload): set when it sends Ugc 1, refused (result 4) while
    // set, cleared by Ugc 0 (FUN_14188f450 ends in UnlockUpload). The file upload after Ugc 2 is not modelled.
    private sealed class ClientLock {
        public bool Locked { get; private set; }

        public bool TryLock() {
            if (Locked) {
                return false;
            }
            Locked = true;
            return true;
        }

        public void Read(IEnumerable<byte[]> sent) {
            if (sent.Any(packet => TestGameSession.Op(packet) == SendOp.Ugc && packet[2] == 0)) {
                Locked = false;
            }
        }
    }

    private UgcHandler Handler(GameSession session) => new() {
        WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance),
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        World = null!,
    };

    private GameSession Start(MeretMarketCurrencyType currency, bool fullOutfitBag = false) {
        if (fullOutfitBag) {
            ItemMetadata outfit = TestGameSession.Metadata(Design + 1, EquipSlot.CL, type: 1);
            test.Store(outfit with { Property = outfit.Property with { IsSkin = true } });
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabSkinCount = [1, 0] });
        }
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, currency, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CL));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        test.TakeSent();
        return session;
    }

    // Ugc 1: the upload control's item uid, the upload head (kind 1, an item), then the item's part.
    private static ByteReader Upload() => TestGameSession.Packet(writer => {
        writer.WriteByte(1);
        writer.WriteLong(0);
        writer.WriteByte(1);
        writer.WriteShort(0);
        writer.WriteInt(0);
        writer.WriteLong(0);
        writer.WriteLong(0);
        writer.WriteUnicodeString("");
        writer.WriteUnicodeString("");
        writer.WriteLong(0);
        writer.WriteUnicodeString("");
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteUnicodeString("");
        writer.WriteLong(0);
        writer.WriteInt(Design);
        writer.WriteInt(1);
        writer.WriteUnicodeString("shirt");
        writer.WriteByte(0);
        writer.WriteLong(390);
        writer.WriteBool(false);
    });

    [Test]
    public void ErrorPacket_IsModeZero_KindThenError() {
        byte[] packet = Maple2.Server.Core.Packets.UgcPacket.Error(UgcType.Item, UgcError.s_err_lack_meso).ToArray();
        Assert.That(TestGameSession.Op(packet), Is.EqualTo(SendOp.Ugc));
        Assert.That(packet.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, 1, 0x13 }));
    }

    [Test]
    public void LackOfMeso_SendsUgcZero_AndTheNextUploadIsNotRefused() {
        GameSession session = Start(MeretMarketCurrencyType.Meso);
        session.Currency.Meso = 100;
        var client = new ClientLock();

        Assert.That(client.TryLock(), Is.True);
        Handler(session).Handle(session, Upload());
        List<byte[]> sent = test.TakeSent();
        client.Read(sent);

        // One text: the client's own for Ugc 0 0x13, no message box beside it; nothing charged.
        byte[] error = sent.Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);
        Assert.That(error.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, 1, (byte) UgcError.s_err_lack_meso }));
        Assert.That(sent.Select(TestGameSession.Op), Has.No.Member(SendOp.Notice));
        Assert.That(session.Currency.Meso, Is.EqualTo(100));
        Assert.That(client.Locked, Is.False);

        session.Currency.Meso = 5000;
        Assert.That(client.TryLock(), Is.True);
        Handler(session).Handle(session, Upload());
        byte[] upload = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);
        Assert.That(upload[2], Is.EqualTo(2));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 390));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void LackOfMeret_KeepsItsMessageBox_AndUnlocksSilently() {
        GameSession session = Start(MeretMarketCurrencyType.Meret);
        session.Currency.Meret = 100;

        Handler(session).Handle(session, Upload());
        List<byte[]> sent = test.TakeSent();

        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Notice), Is.EqualTo(1));
        byte[] error = sent.Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);
        Assert.That(error.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, 1, (byte) UgcError.Refused }));
        Assert.That(session.Currency.Meret, Is.EqualTo(100));
    }

    [Test]
    public void FullOutfitBag_SendsTheInventoryText() {
        GameSession session = Start(MeretMarketCurrencyType.Meso, fullOutfitBag: true);
        session.Currency.Meso = 5000;
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Outfit), Is.EqualTo(0));

        Handler(session).Handle(session, Upload());

        byte[] error = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);
        Assert.That(error.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, 1, (byte) UgcError.s_err_inventory }));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
    }
}
