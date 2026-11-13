using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A design upload is paid from the wallet the client checks for the design's priceType (ConvertPriceType,
// ugccubecategorytable.cpp:445): 2 the red merets only, 3 the red merets when they cover the price, else the
// plain ones. A failed upload (Ugc 0) gives the fee back to the wallet that paid it. The item id is made up.
public class UgcUploadWalletTests {
    private const int Design = 11300903;
    private const int Town = 90000100;
    private const long Price = 390;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private UgcHandler Handler(GameSession session) => new() {
        WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance),
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        World = null!,
    };

    private GameSession Start(byte priceType, long meret, long red) {
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, (MeretMarketCurrencyType) priceType, (int) Price, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CL));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meret = meret;
        session.Currency.GameMeret = red;
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
        writer.WriteLong(Price);
        writer.WriteBool(false);
    });

    // Ugc 0: the client's upload failed after the server answered.
    private static ByteReader UploadFailed() => TestGameSession.Packet(writer => writer.WriteByte(0));

    private byte[] Uploaded() {
        byte[] upload = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);
        Assert.That(upload[2], Is.EqualTo(2));
        return upload;
    }

    private void AssertRefusedWith(StringCode text) {
        List<byte[]> sent = test.TakeSent();
        byte[] error = sent.Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);
        Assert.That(error.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, 1, (byte) UgcError.Refused }));
        byte[] notice = sent.Single(packet => TestGameSession.Op(packet) == SendOp.Notice);
        Assert.That(notice, Is.EqualTo(NoticePacket.MessageBox(text).ToArray()));
    }

    [Test]
    public void PriceType3_RedCoversThePrice_TakesRed_AndGivesItBackToRed() {
        GameSession session = Start(3, meret: 1000, red: 500);
        UgcHandler handler = Handler(session);

        handler.Handle(session, Upload());
        Uploaded();
        Assert.That(session.Currency.GameMeret, Is.EqualTo(500 - Price));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));

        handler.Handle(session, UploadFailed());
        Assert.That(session.Currency.GameMeret, Is.EqualTo(500));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        // Once only.
        handler.Handle(session, UploadFailed());
        Assert.That(session.Currency.GameMeret, Is.EqualTo(500));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void PriceType3_RedShort_TakesPlain_AndGivesItBackToPlain() {
        GameSession session = Start(3, meret: 1000, red: Price - 1);
        UgcHandler handler = Handler(session);

        handler.Handle(session, Upload());
        Uploaded();
        Assert.That(session.Currency.Meret, Is.EqualTo(1000 - Price));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(Price - 1));

        handler.Handle(session, UploadFailed());
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(Price - 1));
    }

    [Test]
    public void PriceType3_BothShort_SendsThePlainMeretText_AndTakesNothing() {
        GameSession session = Start(3, meret: Price - 1, red: Price - 1);

        Handler(session).Handle(session, Upload());

        AssertRefusedWith(StringCode.s_err_lack_merat);
        Assert.That(session.Currency.Meret, Is.EqualTo(Price - 1));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(Price - 1));
    }

    [Test]
    public void PriceType2_RedShort_IsRefusedWithTheRedText_ThoughPlainCovers() {
        GameSession session = Start(2, meret: 1000, red: Price - 1);

        Handler(session).Handle(session, Upload());

        AssertRefusedWith(StringCode.s_err_lack_merat_red);
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(Price - 1));
    }

    [Test]
    public void PriceType1_TakesPlain_AndGivesItBackToPlain() {
        GameSession session = Start(1, meret: 1000, red: 1000);
        UgcHandler handler = Handler(session);

        handler.Handle(session, Upload());
        Uploaded();
        Assert.That(session.Currency.Meret, Is.EqualTo(1000 - Price));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(1000));

        handler.Handle(session, UploadFailed());
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(1000));
    }
}
