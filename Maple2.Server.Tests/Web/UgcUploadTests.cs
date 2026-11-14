using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Tests.Game;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Web;

// The UGC upload (urq.aspx). The client posts the file with only its ids (CSoapClient::UploadUgcFile) and never a
// file of 0 bytes or of 1 MiB or more (UploadPendingUgc). Every kind but the profile picture takes the upload the game
// server opened when it made the resource for the uploading character (UgcHandler: CreateUgc with the upload window),
// each file once, under the folder the game server recorded. The profile picture, which the client posts without
// asking the game server, needs that character of that account in game and one picture per gap.
public class UgcUploadTests {
    private const long Account = 10000000001, Owner = 20000000001, Item = 11300902;
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static UgcResource Resource(UgcType type, long owner = Owner, DateTime? until = null, byte parts = 0, long target = Item) =>
        new() { Id = 77, Type = type, OwnerId = owner, UploadUntil = until ?? Now.AddMinutes(1), UploadedParts = parts, TargetId = target };

    private static string? Refusal(UgcType kind, UgcResource? resource, long character = Owner, long account = Account, int size = 100) =>
        WebController.UploadRefusal(kind, account, character, resource, size, Now);

    [TestCase(UgcType.Item, UgcType.Item)]
    [TestCase(UgcType.Furniture, UgcType.Furniture)]
    [TestCase(UgcType.Mount, UgcType.Mount)]
    [TestCase(UgcType.ItemIcon, UgcType.Furniture)]
    [TestCase(UgcType.Banner, UgcType.Banner)]
    [TestCase(UgcType.GuildEmblem, UgcType.GuildEmblem)]
    [TestCase(UgcType.GuildBanner, UgcType.GuildBanner)]
    [TestCase(UgcType.LayoutBlueprint, UgcType.LayoutBlueprint)]
    [TestCase(UgcType.BlueprintIcon, UgcType.LayoutBlueprint)]
    public void TheOwnersOpenUploadOfTheKind_IsTaken(UgcType kind, UgcType resourceType) {
        Assert.That(Refusal(kind, Resource(resourceType)), Is.Null);
    }

    [Test]
    public void AnotherCharactersResource_IsRefused() {
        Assert.That(Refusal(UgcType.GuildEmblem, Resource(UgcType.GuildEmblem), character: Owner + 1), Is.EqualTo("Not the resource's owner."));
        Assert.That(Refusal(UgcType.Item, Resource(UgcType.Item, owner: 0)), Is.EqualTo("Not the resource's owner."));
    }

    [TestCase(UgcType.GuildEmblem, UgcType.Banner)]
    [TestCase(UgcType.Banner, UgcType.GuildBanner)]
    [TestCase(UgcType.Item, UgcType.LayoutBlueprint)]
    [TestCase(UgcType.ItemIcon, UgcType.Banner)]
    [TestCase(UgcType.BlueprintIcon, UgcType.Item)]
    public void AResourceOfAnotherKind_IsRefused(UgcType kind, UgcType resourceType) {
        Assert.That(Refusal(kind, Resource(resourceType)), Is.EqualTo("Wrong UGC resource type."));
    }

    [TestCase(UgcType.Banner)]
    [TestCase(UgcType.GuildEmblem)]
    [TestCase(UgcType.Item)]
    public void NoResource_IsRefused(UgcType kind) {
        Assert.That(Refusal(kind, null), Is.EqualTo("Invalid UGC resource."));
    }

    // A resource whose upload was never opened (a row from before, or one no upload made) or whose window is over.
    [Test]
    public void AResourceWithoutAnOpenUpload_IsRefused() {
        Assert.That(Refusal(UgcType.Banner, new UgcResource { Id = 77, Type = UgcType.Banner, OwnerId = Owner }), Is.EqualTo("No upload open."));
        Assert.That(Refusal(UgcType.Banner, Resource(UgcType.Banner, until: Now.AddSeconds(-1))), Is.EqualTo("No upload open."));
    }

    // Each file once: the icon and the file are taken apart.
    [Test]
    public void ATakenFile_IsRefused() {
        Assert.That(Refusal(UgcType.ItemIcon, Resource(UgcType.Item, parts: UgcRules.UploadIcon)), Is.EqualTo("Upload already taken."));
        Assert.That(Refusal(UgcType.Item, Resource(UgcType.Item, parts: UgcRules.UploadIcon)), Is.Null);
        Assert.That(Refusal(UgcType.Banner, Resource(UgcType.Banner, parts: UgcRules.UploadFile)), Is.EqualTo("Upload already taken."));
    }

    // The client's bounds: nSize == 0 || nSize >= 0x100000 is never sent.
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(0xFFFFF, true)]
    [TestCase(0x100000, false)]
    public void TheFileSize(int size, bool taken) {
        Assert.That(Refusal(UgcType.Banner, Resource(UgcType.Banner), size: size) == null, Is.EqualTo(taken));
        Assert.That(Refusal(UgcType.ProfileAvatar, null, size: size) == null, Is.EqualTo(taken));
    }

    // A profile picture names a real account and character: GetCharacter tests the account only for one of 0 or more,
    // so an account of -1 would take any character.
    [TestCase(-1, Owner)]
    [TestCase(0, Owner)]
    [TestCase(Account, 0)]
    [TestCase(Account, -1)]
    public void AProfilePictureWithoutRealIds_IsRefused(long account, long character) {
        Assert.That(Refusal(UgcType.ProfileAvatar, null, character: character, account: account), Is.EqualTo("Invalid character."));
    }

    // The request is capped at the header and the largest file the client sends.
    [Test]
    public void TheRequestSizeLimit() {
        RequestSizeLimitAttribute? limit = typeof(WebController).GetMethod(nameof(WebController.Upload))!.GetCustomAttributes<RequestSizeLimitAttribute>().SingleOrDefault();
        Assert.That(limit, Is.Not.Null);
        Assert.That(((IRequestSizeLimitMetadata) limit!).MaxRequestBodySize, Is.EqualTo(48 + 0xFFFFF));
    }

    // The ranking (irrq) and info (ruq) requests are capped at 64 KiB: a search text of 32,000 characters still fits.
    [TestCase(nameof(WebController.Rankings))]
    [TestCase(nameof(WebController.Info))]
    public void TheSmallRequestsSizeLimit(string method) {
        RequestSizeLimitAttribute? limit = typeof(WebController).GetMethod(method)!.GetCustomAttributes<RequestSizeLimitAttribute>().SingleOrDefault();
        Assert.That(limit, Is.Not.Null);
        Assert.That(((IRequestSizeLimitMetadata) limit!).MaxRequestBodySize, Is.EqualTo(0x10000));
        Assert.That(0x10000, Is.GreaterThanOrEqualTo(40 + 32000 * 2));
    }

    // The folder is the one the game server recorded (TargetId), and the key the one the GET routes serve.
    [Test]
    public void TheFolderIsTheResourcesTarget() {
        Assert.That(WebController.Target(UgcType.Item, Owner, Resource(UgcType.Item), "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("items", "11300902"), "77.m2u", "item/ms2/01/11300902/77.m2u", true)));
        Assert.That(WebController.Target(UgcType.ItemIcon, Owner, Resource(UgcType.Item), "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("itemicon", "11300902"), "77.png", "itemicon/ms2/01/11300902/77.png", false)));
        Assert.That(WebController.Target(UgcType.GuildEmblem, Owner, Resource(UgcType.GuildEmblem, target: 5), "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("guildmark", "5"), "77.png", "guildmark/ms2/01/5/77.png", true)));
        Assert.That(WebController.Target(UgcType.GuildBanner, Owner, Resource(UgcType.GuildBanner, target: 5), "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("guildmark", "5", "banner"), "77.png", "guildmark/ms2/01/5/banner/77.png", true)));
        Assert.That(WebController.Target(UgcType.Banner, Owner, Resource(UgcType.Banner, target: 3000), "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("banner", "3000"), "77.m2u", "banner/ms2/01/3000/77.m2u", true)));
        Assert.That(WebController.Target(UgcType.BlueprintIcon, Owner, Resource(UgcType.LayoutBlueprint), "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("blueprint", "77"), "77_icon.png", "blueprint/ms2/01/77/77_icon.png", false)));
        Assert.That(WebController.Target(UgcType.ProfileAvatar, Owner, null, "g"),
            Is.EqualTo(new WebController.UploadTarget(Path.Combine("profiles", Owner.ToString()), "g.png", $"data/profiles/avatar/{Owner}/g.png", false)));
    }

    private static WebStorage Web() => new(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance);

    // The storage takes each file of an open upload once, so a second upload to the same resource is refused; a
    // resource never opened takes nothing.
    [Test]
    public void TakeUpload_OncePerFile() {
        WebStorage web = Web();
        long open, never;
        using (WebStorage.Request db = web.Context()) {
            open = db.CreateUgc(UgcType.LayoutBlueprint, Owner, 0, Now.AddMinutes(5))!.Id;
            never = db.CreateUgc(UgcType.LayoutBlueprint, Owner)!.Id;
        }

        bool Take(long id, byte part) {
            using WebStorage.Request db = web.Context();
            return db.TakeUpload(id, part, Now);
        }

        Assert.That(Take(never, UgcRules.UploadFile), Is.False);
        Assert.That(Take(open, UgcRules.UploadIcon), Is.True);
        Assert.That(Take(open, UgcRules.UploadIcon), Is.False);
        Assert.That(Take(open, UgcRules.UploadFile), Is.True);
        Assert.That(Take(open, UgcRules.UploadFile), Is.False);
        using WebStorage.Request read = web.Context();
        UgcResource resource = read.GetUgc(open)!;
        Assert.That((resource.UploadUntil, resource.UploadedParts, resource.OwnerId), Is.EqualTo(((DateTime?) Now.AddMinutes(5), (byte) 3, Owner)));
        Assert.That(WebController.UploadRefusal(UgcType.LayoutBlueprint, 1, Owner, resource, 10, Now), Is.EqualTo(WebController.UploadTaken));
    }

    // A file that could not be written gives its part back: the client's next try takes it; a part never taken is
    // not given back.
    [Test]
    public void GiveBackUpload_TheNextTryTakesIt() {
        WebStorage web = Web();
        using WebStorage.Request db = web.Context();
        long id = db.CreateUgc(UgcType.Item, Owner, Item, Now.AddMinutes(5))!.Id;
        Assert.That(db.TakeUpload(id, UgcRules.UploadIcon, Now), Is.True);
        Assert.That(db.TakeUpload(id, UgcRules.UploadFile, Now), Is.True);
        Assert.That(db.GiveBackUpload(id, UgcRules.UploadFile), Is.True);
        Assert.That(db.GiveBackUpload(id, UgcRules.UploadFile), Is.False);
        Assert.That(db.GetUgc(id)!.UploadedParts, Is.EqualTo(UgcRules.UploadIcon));
        Assert.That(db.TakeUpload(id, UgcRules.UploadIcon, Now), Is.False);
        Assert.That(db.TakeUpload(id, UgcRules.UploadFile, Now), Is.True);
    }

    // The client's retry of a stored file (its answer lost): the same bytes get the stored key; other bytes, or
    // nothing stored, get none. A profile picture compares its folder's one file.
    [Test]
    public void StoredKey_SameBytesOnly() {
        string root = Path.Combine(Path.GetTempPath(), "ugc-" + Guid.NewGuid());
        try {
            var resource = new UgcResource { Id = 77, OwnerId = Owner, Type = UgcType.Item, TargetId = Item };
            byte[] bytes = [1, 2, 3];
            WebController.UploadTarget target = WebController.Target(UgcType.Item, Owner, resource, "")!;
            Assert.That(WebController.StoredKey(root, UgcType.Item, Owner, resource, bytes), Is.Null);
            Directory.CreateDirectory(Path.Combine(root, target.Folder));
            System.IO.File.WriteAllBytes(Path.Combine(root, target.Folder, target.File), bytes);
            Assert.That(WebController.StoredKey(root, UgcType.Item, Owner, resource, bytes), Is.EqualTo($"item/ms2/01/{Item}/77.m2u"));
            Assert.That(WebController.StoredKey(root, UgcType.Item, Owner, resource, [1, 2, 4]), Is.Null);
            Assert.That(WebController.StoredKey(root, UgcType.Item, Owner, resource, [1, 2]), Is.Null);
            Assert.That(WebController.StoredKey(root, UgcType.ItemIcon, Owner, resource, bytes), Is.Null);

            string profiles = Path.Combine(root, "profiles", Owner.ToString());
            Directory.CreateDirectory(profiles);
            System.IO.File.WriteAllBytes(Path.Combine(profiles, "g.png"), bytes);
            Assert.That(WebController.StoredKey(root, UgcType.ProfileAvatar, Owner, null, bytes), Is.EqualTo($"data/profiles/avatar/{Owner}/g.png"));
            Assert.That(WebController.StoredKey(root, UgcType.ProfileAvatar, Owner, null, [9]), Is.Null);
            Assert.That(WebController.StoredKey(root, UgcType.ProfileAvatar, Owner + 1, null, bytes), Is.Null);
        } finally {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void TakeUpload_AfterTheWindow() {
        WebStorage web = Web();
        using WebStorage.Request db = web.Context();
        long id = db.CreateUgc(UgcType.Banner, Owner, 3000, Now.AddMinutes(-1))!.Id;
        Assert.That(db.TakeUpload(id, UgcRules.UploadFile, Now), Is.False);
    }

    // The profile picture: one per character per gap (2 s, the client's 3 tries are 500 ms apart); a turn whose file
    // was not written is given back, but not one a later upload took since.
    [Test]
    public void ProfilePicture_OnePerGap() {
        long character = 20000000000 + Random.Shared.Next(1, 1_000_000);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character, Now), Is.True);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character, Now + ProfileUploads.Gap - TimeSpan.FromSeconds(1)), Is.False);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character + 1, Now), Is.True);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character, Now + ProfileUploads.Gap), Is.True);
        Assert.That(ProfileUploads.Gap, Is.EqualTo(TimeSpan.FromSeconds(2)));

        DateTime later = Now + ProfileUploads.Gap;
        ProfileUploads.GiveBack(UgcType.ProfileAvatar, character, later);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character, later + TimeSpan.FromMilliseconds(500)), Is.True);
        ProfileUploads.GiveBack(UgcType.ProfileAvatar, character, later);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character, later + TimeSpan.FromSeconds(1)), Is.False);
    }

    // The wedding profile picture takes its own turns: a profile picture just before does not hold it back.
    [Test]
    public void WeddingProfile_OwnTurns() {
        long character = 20000000000 + Random.Shared.Next(1_000_001, 2_000_000);
        Assert.That(ProfileUploads.Take(UgcType.ProfileAvatar, character, Now), Is.True);
        Assert.That(ProfileUploads.Take(UgcType.WeddingProfile, character, Now), Is.True);
        Assert.That(ProfileUploads.Take(UgcType.WeddingProfile, character, Now + TimeSpan.FromSeconds(1)), Is.False);
    }

    // Kind 10 has no resource, like the profile picture: real ids and the file size only here (the controller then
    // needs the character in game and married). Its file goes to its own folder under its own key.
    [Test]
    public void WeddingProfile_RefusalAndTarget() {
        Assert.That(WebController.UploadRefusal(UgcType.WeddingProfile, 1, Owner, null, 10, Now), Is.Null);
        Assert.That(WebController.UploadRefusal(UgcType.WeddingProfile, 0, Owner, null, 10, Now), Is.Not.Null);
        Assert.That(WebController.UploadRefusal(UgcType.WeddingProfile, 1, 0, null, 10, Now), Is.Not.Null);
        Assert.That(WebController.UploadRefusal(UgcType.WeddingProfile, 1, Owner, null, WebController.MaxUploadFileSize + 1, Now), Is.Not.Null);
        Assert.That(WebController.UploadRefusal(UgcType.WeddingProfile, 1, Owner, null, 0, Now), Is.Not.Null);

        WebController.UploadTarget target = WebController.Target(UgcType.WeddingProfile, Owner, null, "n")!;
        Assert.That(target, Is.EqualTo(new WebController.UploadTarget(Path.Combine("weddingprofiles", Owner.ToString()), "n.png",
            $"data/profiles/wedding/{Owner}/n.png", false)));
        Assert.That(Maple2.Model.Validators.UgcRules.WeddingProfilePathValid(target.Key, Owner), Is.True);
        Assert.That(ProfileController.PicturePath("D", "weddingprofiles", Owner, "n.png"), Is.EqualTo(Path.Combine("D", target.Folder, target.File)));

        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            byte[] bytes = [1, 2, 3];
            string folder = Path.Combine(root, target.Folder);
            Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(Path.Combine(folder, "g.png"), bytes);
            Assert.That(WebController.StoredKey(root, UgcType.WeddingProfile, Owner, null, bytes), Is.EqualTo($"data/profiles/wedding/{Owner}/g.png"));
            Assert.That(WebController.StoredKey(root, UgcType.ProfileAvatar, Owner, null, bytes), Is.Null);
        } finally {
            Directory.Delete(root, true);
        }
    }

    // In game: the account online and the character in a channel (the session's entry stamps both, LoadPlayer); a
    // character of another account, or none, is not.
    [Test]
    public void ProfilePicture_InGame() {
        var test = new TestGameSession();
        test.Start();
        using var db = test.Storage.Context();
        Assert.That(db.InGame(test.AccountId, test.CharacterId), Is.True);
        Assert.That(db.InGame(test.AccountId + 1, test.CharacterId), Is.False);
        Assert.That(db.InGame(test.AccountId, test.CharacterId + 1), Is.False);
        Assert.That(db.InGame(-1, test.CharacterId), Is.False);
    }
}
