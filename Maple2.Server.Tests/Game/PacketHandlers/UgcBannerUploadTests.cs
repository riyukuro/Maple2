using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The ad banner upload (Ugc 1, kind 3, CUgcBannerProxy::Write): i64 banner id, u8 count, then each reserved hour as the
// reservation list sends it (i64 slot uid, i32, i64, i32 date, i32 hour, i64). The slots are the ones the character
// reserved (Ugc 19); every hour is priced from the banner table and the merets are taken once for all of them. Ugc 0
// after a paid upload gives the merets back and takes the design off the slots. The banner and prices are made up.
public class UgcBannerUploadTests {
    private const int Town = 90000100;
    private const long BannerId = 7001;
    private const long Price = 50;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private UgcHandler handler = null!;
    private FieldUgcBanner banner = null!;
    private long nextSlotId = 501;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new BannerTable([new BannerTable.Entry(BannerId, Town, Enumerable.Repeat(Price, 24).ToList())]));
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        typeof(FieldManager).GetField(nameof(FieldManager.Banners))!.SetValue(session.Field,
            new System.Collections.Concurrent.ConcurrentDictionary<long, FieldUgcBanner>());
        banner = new FieldUgcBanner(session.Field!, BannerId, Town, []);
        session.Field!.Banners[BannerId] = banner;
        session.Currency.Meret = 1000;
        test.TakeSent();
        handler = new UgcHandler {
            WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                NullLogger<WebStorage>.Instance),
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            World = null!,
        };
    }

    // A saved slot (nonzero id) at a later hour, reserved by the given owner.
    private BannerSlot Slot(long ownerId, int hoursLater = 2) {
        DateTime when = DateTime.UtcNow.AddHours(hoursLater);
        var slot = new BannerSlot(BannerId, when.Year * 10000 + when.Month * 100 + when.Day, when.Hour) {
            Id = nextSlotId++,
            OwnerId = ownerId,
        };
        lock (banner.Slots) {
            banner.Slots.Add(slot);
        }
        return slot;
    }

    private void Upload(long bannerId, params BannerSlot[] slots) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(0);
            writer.WriteByte((byte) UgcType.Banner);
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
            writer.WriteLong(bannerId);
            writer.WriteByte((byte) slots.Length);
            foreach (BannerSlot slot in slots) {
                writer.WriteLong(slot.Id);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(slot.Date);
                writer.WriteInt(slot.Hour);
                writer.WriteLong(0);
            }
        }));
    }

    private byte[] UgcReply() => test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Ugc);

    private void AssertRefused(byte[] reply) {
        Assert.That(reply.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) UgcType.Banner, (byte) UgcError.Refused }));
    }

    [Test]
    public void Upload_OwnHours_ChargesEachAndPutsTheDesignOnThem() {
        BannerSlot first = Slot(session.CharacterId, 2), second = Slot(session.CharacterId, 3);

        Upload(BannerId, first, second);

        Assert.Multiple(() => {
            Assert.That(UgcReply()[2], Is.EqualTo(2));
            Assert.That(session.Currency.Meret, Is.EqualTo(1000 - 2 * Price));
            Assert.That(first.Template, Is.Not.Null);
            Assert.That(first.Template!.CharacterId, Is.EqualTo(session.CharacterId));
            Assert.That(second.Template!.Id, Is.EqualTo(first.Template.Id));
            Assert.That(session.StagedBanner, Is.SameAs(banner));
        });
    }

    // A slot whose owner was lost (0) and that has no design takes the upload, and becomes the uploader's.
    [Test]
    public void Upload_AnOwnerlessHour_IsTakenByTheUploader() {
        BannerSlot slot = Slot(0);

        Upload(BannerId, slot);

        Assert.That(slot.OwnerId, Is.EqualTo(session.CharacterId));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000 - Price));
    }

    [Test]
    public void Upload_AnotherCharactersHour_IsRefusedWithNothingTaken() {
        BannerSlot own = Slot(session.CharacterId, 2), other = Slot(session.CharacterId + 99, 3);

        Upload(BannerId, own, other);

        AssertRefused(UgcReply());
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(own.Template, Is.Null);
            Assert.That(other.OwnerId, Is.EqualTo(session.CharacterId + 99));
            Assert.That(session.StagedBanner, Is.Null);
        });
    }

    [Test]
    public void Upload_TheSameHourTwice_IsRefused() {
        BannerSlot slot = Slot(session.CharacterId);

        Upload(BannerId, slot, slot);

        AssertRefused(UgcReply());
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
    }

    [Test]
    public void Upload_AnUnsavedSlot_IsRefused() {
        BannerSlot slot = Slot(session.CharacterId);
        slot.Id = 0;

        Upload(BannerId, slot);

        AssertRefused(UgcReply());
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
    }

    [Test]
    public void Upload_NoHours_IsRefused() {
        Upload(BannerId);

        AssertRefused(UgcReply());
    }

    [Test]
    public void Upload_ABannerNotOnTheField_IsRefused() {
        BannerSlot slot = Slot(session.CharacterId);

        Upload(BannerId + 1, slot);

        AssertRefused(UgcReply());
        Assert.That(slot.Template, Is.Null);
    }

    [Test]
    public void Upload_ABannerWithoutAPrice_IsRefused() {
        var unpriced = new FieldUgcBanner(session.Field!, BannerId + 1, Town, []);
        session.Field!.Banners[BannerId + 1] = unpriced;

        Upload(BannerId + 1, Slot(session.CharacterId));

        AssertRefused(UgcReply());
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
    }

    [Test]
    public void Upload_TooFewMerets_ShowsTheLackTextAndTakesNothing() {
        session.Currency.Meret = Price - 1;
        BannerSlot slot = Slot(session.CharacterId);

        Upload(BannerId, slot);
        List<byte[]> sent = test.TakeSent();

        Assert.Multiple(() => {
            Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Notice), Is.EqualTo(1));
            Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Ugc), Is.EqualTo(1));
            Assert.That(session.Currency.Meret, Is.EqualTo(Price - 1));
            Assert.That(slot.Template, Is.Null);
        });
    }

    // Ugc 0 after the upload: the web upload failed, so the merets come back and the design without a picture comes
    // off; the hours stay the character's.
    [Test]
    public void UploadFailed_AfterABannerUpload_GivesTheMeretsBackAndClearsTheDesign() {
        BannerSlot slot = Slot(session.CharacterId);
        Upload(BannerId, slot);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(slot.Template, Is.Null);
            Assert.That(slot.OwnerId, Is.EqualTo(session.CharacterId));
            Assert.That(session.StagedBanner, Is.Null);
        });
    }

    // A second upload with the first still pending gives the first one's merets back before it charges, so the hours
    // cost once; the failed design without a picture is replaced by its own character.
    [Test]
    public void Upload_Again_ChargesOnce() {
        BannerSlot slot = Slot(session.CharacterId);
        Upload(BannerId, slot);
        test.TakeSent();

        Upload(BannerId, slot);

        Assert.That(UgcReply()[2], Is.EqualTo(2));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000 - Price));
    }
}
