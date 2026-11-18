using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game.Ugc;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The small Ugc commands: the profile picture (11), the wedding profile (12), the banner list (18) and the banner
// reservation (19).
public class UgcSmallCommandTests {
    private const int Town = 90000100;
    private const long BannerId = 7001;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private UgcHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        // The test field is built without its constructor: the banner list is not made.
        typeof(FieldManager).GetField(nameof(FieldManager.Banners))!.SetValue(session.Field,
            new System.Collections.Concurrent.ConcurrentDictionary<long, FieldUgcBanner>());
        test.TakeSent();
        handler = new UgcHandler {
            WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                NullLogger<WebStorage>.Instance),
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            World = null!,
        };
    }

    private string Own(string file) => $"data/profiles/avatar/{session.CharacterId}/{file}.png";

    private void Picture(string path) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(11);
            writer.WriteUnicodeString(path);
        }));
    }

    [Test]
    public void ProfilePicture_OwnPath_IsKept() {
        string path = Own("abc-123");
        Picture(path);
        Assert.That(session.Player.Value.Character.Picture, Is.EqualTo(path));
        // Stored at once: the web server keeps the stored path's file when the next picture is uploaded.
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCharacterPicture(session.CharacterId), Is.EqualTo(path));
    }

    [TestCase("data/profiles/avatar/987654/other.png")] // another character's folder
    [TestCase("data/profiles/wedding/{id}/x.png")] // the wedding folder
    [TestCase("data/profiles/avatar/{id}/x.jpg")]
    [TestCase("data/profiles/avatar/{id}/../x.png")]
    [TestCase("data/profiles/avatar/{id}/.png")]
    [TestCase("")]
    public void ProfilePicture_BadPath_Ignored(string path) {
        string before = session.Player.Value.Character.Picture;
        Picture(path.Replace("{id}", session.CharacterId.ToString()));
        Assert.That(session.Player.Value.Character.Picture, Is.EqualTo(before));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCharacterPicture(session.CharacterId), Is.EqualTo(before));
    }

    [Test]
    public void WeddingProfile_NotMarried_Ignored() {
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteUnicodeString($"data/profiles/wedding/{session.CharacterId}/abc.png");
        })));
    }

    private FieldUgcBanner AddBanner(long id, params BannerSlot[] slots) {
        var banner = new FieldUgcBanner(session.Field!, id, Town, [.. slots]);
        session.Field!.Banners[id] = banner;
        return banner;
    }

    private static (int Date, int Hour) Later(int hours) {
        DateTime when = DateTime.UtcNow.AddHours(hours);
        return (when.Year * 10000 + when.Month * 100 + when.Day, when.Hour);
    }

    private void Reserve(long bannerId, int count, params (int Date, int Hour)[] asked) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(19);
            writer.WriteLong(bannerId);
            writer.WriteInt(count);
            foreach ((int date, int hour) in asked) {
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(date);
                writer.WriteInt(hour);
                writer.WriteLong(0);
            }
        }));
    }

    [Test]
    public void LoadBanners_AnswersOnce() {
        AddBanner(BannerId);
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(18)));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Reserve_FreeHours_AreTakenByTheCharacter() {
        FieldUgcBanner banner = AddBanner(BannerId);
        (int Date, int Hour) first = Later(2), second = Later(3);
        Reserve(BannerId, 2, first, second);

        Assert.Multiple(() => {
            Assert.That(banner.Slots, Has.Count.EqualTo(2));
            Assert.That(banner.Slots.All(slot => slot.OwnerId == session.CharacterId), Is.True);
            Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Reserve_PastHour_Skipped() {
        FieldUgcBanner banner = AddBanner(BannerId);
        Reserve(BannerId, 1, Later(-5));
        Assert.That(banner.Slots, Is.Empty);
    }

    [Test]
    public void Reserve_HourOwnedByAnother_NotGiven() {
        (int Date, int Hour) hour = Later(2);
        var taken = new BannerSlot(BannerId, hour.Date, hour.Hour) { OwnerId = session.CharacterId + 99 };
        FieldUgcBanner banner = AddBanner(BannerId, taken);
        Reserve(BannerId, 1, hour);
        Assert.That(taken.OwnerId, Is.EqualTo(session.CharacterId + 99));
        Assert.That(banner.Slots, Has.Count.EqualTo(1));
    }

    [Test]
    public void Reserve_UnownedHour_IsClaimed() {
        (int Date, int Hour) hour = Later(2);
        var lost = new BannerSlot(BannerId, hour.Date, hour.Hour);
        AddBanner(BannerId, lost);
        Reserve(BannerId, 1, hour);
        Assert.That(lost.OwnerId, Is.EqualTo(session.CharacterId));
    }

    [TestCase(-1)]
    [TestCase(25)]
    public void Reserve_BadCount_Ignored(int count) {
        FieldUgcBanner banner = AddBanner(BannerId);
        Reserve(BannerId, count, Later(2));
        Assert.Multiple(() => {
            Assert.That(banner.Slots, Is.Empty);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void Reserve_UnknownBanner_Ignored() {
        Reserve(424242, 1, Later(2));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Reserve_StopsAtTheCharactersMax() {
        FieldUgcBanner banner = AddBanner(BannerId);
        for (int i = 0; i < 24; i++) {
            (int Date, int Hour) hour = Later(2 + i);
            banner.Slots.Add(new BannerSlot(BannerId, hour.Date, hour.Hour) { OwnerId = session.CharacterId });
        }
        Reserve(BannerId, 1, Later(40));
        Assert.That(banner.Slots, Has.Count.EqualTo(24));
    }
}
