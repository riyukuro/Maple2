using System;
using System.IO;
using Maple2.Server.Web.Controllers;

namespace Maple2.Server.Tests.Web;

// The GET routes serve only the files the uploads wrote: a resource id, a guid or "<id>_icon" under the route's
// folder. A decoded segment with a path in it ("..\x" is a separator on a Windows host) or a dot is refused.
public class WebFilesTests {
    [TestCase("123")]
    [TestCase("123_icon")]
    [TestCase("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [TestCase("homeproduct_1")]
    public void PlainName_IsServedFromItsFolder(string name) {
        Assert.That(WebFiles.StoredPath("D", name, ".png", "itemicon", "5"), Is.EqualTo(Path.Combine("D", "itemicon", "5", name + ".png")));
    }

    [TestCase("..\\..\\x")]
    [TestCase("../x")]
    [TestCase("..")]
    [TestCase("a.b")]
    [TestCase("")]
    [TestCase("C:\\x")]
    public void NameWithAPath_IsRefused(string name) {
        Assert.That(WebFiles.StoredPath("D", name, ".png", "itemicon", "5"), Is.Null);
        Assert.That(WebFiles.Serve(null, "image/png"), Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.NotFound>());
    }

    // A system banner is an operator's file: its row's URL may name it with dots (a dot is no separator).
    [Test]
    public void OperatorName_MayHaveDots() {
        Assert.That(WebFiles.OperatorPath("D", "event.2026", ".png", "system", "banner"), Is.EqualTo(Path.Combine("D", "system", "banner", "event.2026.png")));
        Assert.That(WebFiles.OperatorPath("D", "..\\x", ".png", "system", "banner"), Is.Null);
        Assert.That(WebFiles.OperatorPath("D", "../x", ".png", "system", "banner"), Is.Null);
        Assert.That(WebFiles.StoredPath("D", "event.2026", ".png", "system", "banner"), Is.Null);
        foreach (string name in new[] { ".", "..", "...", ".hidden", "", new string('a', 65) }) {
            Assert.That(WebFiles.OperatorPath("D", name, ".png", "system", "banner"), Is.Null, name);
        }
        Assert.That(WebFiles.OperatorFileName("a."), Is.True);
        Assert.That(WebFiles.OperatorFileName(new string('a', 64)), Is.True);
    }

    [Test]
    public void LongName_IsRefused() {
        Assert.That(WebFiles.PlainName(new string('a', 64)), Is.True);
        Assert.That(WebFiles.PlainName(new string('a', 65)), Is.False);
    }
}

// The upload's file must be of its kind's format: the zlib stream of a design or banner (.m2u), the PNG of every
// picture and icon (the client re-encodes each upload, CUgcUploadCtrl::PrepareUploadByKind).
public class UploadFileTypeTests {
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static byte[] Zlib(int level) {
        using var stream = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(stream, (System.IO.Compression.CompressionLevel) level)) {
            zlib.Write("DDS "u8);
        }
        return stream.ToArray();
    }

    [TestCase(Maple2.Model.Enum.UgcType.Item)]
    [TestCase(Maple2.Model.Enum.UgcType.Furniture)]
    [TestCase(Maple2.Model.Enum.UgcType.Mount)]
    [TestCase(Maple2.Model.Enum.UgcType.Banner)]
    public void Design_IsAZlibStream(Maple2.Model.Enum.UgcType type) {
        foreach (int level in new[] { 0, 1, 2, 3 }) {
            Assert.That(WebController.FileTypeRefusal(type, Zlib(level)), Is.Null);
        }
        Assert.That(WebController.FileTypeRefusal(type, [0x78, 0x9C]), Is.Null);
        Assert.That(WebController.FileTypeRefusal(type, Png), Is.Not.Null);
        Assert.That(WebController.FileTypeRefusal(type, [0x78]), Is.Not.Null);
        Assert.That(WebController.FileTypeRefusal(type, [0x78, 0x9D]), Is.Not.Null);
        Assert.That(WebController.FileTypeRefusal(type, "<html>"u8), Is.Not.Null);
    }

    [TestCase(Maple2.Model.Enum.UgcType.ProfileAvatar)]
    [TestCase(Maple2.Model.Enum.UgcType.WeddingProfile)]
    [TestCase(Maple2.Model.Enum.UgcType.GuildEmblem)]
    [TestCase(Maple2.Model.Enum.UgcType.GuildBanner)]
    [TestCase(Maple2.Model.Enum.UgcType.LayoutBlueprint)]
    [TestCase(Maple2.Model.Enum.UgcType.ItemIcon)]
    [TestCase(Maple2.Model.Enum.UgcType.BlueprintIcon)]
    public void Picture_IsAPng(Maple2.Model.Enum.UgcType type) {
        Assert.That(WebController.FileTypeRefusal(type, Png), Is.Null);
        Assert.That(WebController.FileTypeRefusal(type, Zlib(1)), Is.Not.Null);
        Assert.That(WebController.FileTypeRefusal(type, Png.AsSpan(0, 7)), Is.Not.Null);
        Assert.That(WebController.FileTypeRefusal(type, [0xFF, 0xD8, 0xFF, 0xE0]), Is.Not.Null);
    }
}
