using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Maple2.Database.Storage;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An upload of a kind that needs a guild or a handler answers Ugc 0 with the kind and the refusal
// error, before any resource is made (the client's upload lock is only cleared by that answer).
public class UgcUploadRefusalTests {
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private UgcHandler Handler(GameSession session) => new() {
        WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance),
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        World = null!,
    };

    // Ugc 1: the upload control's item uid, the upload head of the kind, then the data of the kind.
    private static ByteReader Upload(UgcType type, params long[] data) => TestGameSession.Packet(writer => {
        writer.WriteByte(1);
        writer.WriteLong(0);
        writer.WriteByte((byte) type);
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
        foreach (long value in data) {
            writer.WriteLong(value);
        }
        writer.WriteInt(0);
    });

    private void AssertRefused(UgcType type, UgcError error) {
        byte[][] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Ugc).ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        Assert.That(sent[0].Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) type, (byte) error }));
    }

    [TestCase(UgcType.GuildEmblem)]
    [TestCase(UgcType.GuildBanner)]
    public void GuildKind_WithoutAGuild_IsRefused(UgcType type) {
        GameSession session = Start();

        Handler(session).Handle(session, Upload(type, 7));
        AssertRefused(type, UgcError.Refused);
        Assert.That(session.StagedGuildEmblemUgc, Is.EqualTo(0));
        Assert.That(session.StagedGuildPoster, Is.Null);
    }

    [TestCase(UgcType.ProfileAvatar)]
    [TestCase(UgcType.WeddingProfile)]
    [TestCase(UgcType.None)]
    public void KindWithNoUploadHere_IsRefused(UgcType type) {
        GameSession session = Start();

        Handler(session).Handle(session, Upload(type));
        AssertRefused(type, UgcError.Refused);
    }
}
