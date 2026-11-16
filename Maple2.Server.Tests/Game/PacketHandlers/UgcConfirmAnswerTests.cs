using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client locks its upload when it sends Ugc 1 and only two answers to its Ugc 3 (confirmation) unlock it
// (CUgcManager::OnUgc): Ugc 4 with the kind, the resource id and the stored path (CheckUploadResponse, then
// FUN_141883860, which ends in UnlockUpload), or Ugc 0 (FUN_14188f450). A guild poster pressed again after the first
// upload asserted in SetCurrentUpload because the confirmation of a guild poster was answered with neither.
public class UgcConfirmAnswerTests {
    private TestGameSession test = null!;
    private WebStorage web = null!;

    private GameSession Start() {
        test = new TestGameSession();
        web = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance);
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private UgcHandler Handler(GameSession session) => new() {
        WebStorage = web,
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        World = test.World,
    };

    private ByteReader Confirm(GameSession session, UgcType type, long ugcUid) => TestGameSession.Packet(writer => {
        writer.WriteByte(3);
        writer.WriteByte((byte) type);
        writer.WriteShort(0);
        writer.WriteInt(0);
        writer.WriteLong(session.AccountId);
        writer.WriteLong(session.CharacterId);
        writer.WriteUnicodeString("");
        writer.WriteUnicodeString("");
        writer.WriteLong(ugcUid);
        writer.WriteUnicodeString("");
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteUnicodeString("");
    });

    private UgcResource Resource(UgcType type, long target, string path) {
        using WebStorage.Request request = web.Context();
        UgcResource resource = request.CreateUgc(type, test.CharacterId, target, DateTime.UtcNow.AddHours(1))!;
        request.UpdatePath(resource.Id, path);
        resource.Path = path;
        return resource;
    }

    private void SetGuild(GameSession session) {
        Guild guild;
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Guild", test.CharacterId)!;
        }
        typeof(GuildManager).GetProperty(nameof(GuildManager.Guild))!.SetValue(session.Guild, guild);
    }

    private List<byte[]> Answers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) is SendOp.Ugc or SendOp.Guild).ToList();

    // The poster the guild leader set: the confirmation is answered with Ugc 4 (kind 8, resource id, path).
    [Test]
    public void GuildPoster_Confirmed_IsAnsweredWithTheStoredPath() {
        GameSession session = Start();
        SetGuild(session);
        UgcResource resource = Resource(UgcType.GuildBanner, session.Guild.Id, "ms2/poster.png");
        session.StagedGuildPoster = new GuildPoster { Id = 3, OwnerId = session.CharacterId, OwnerName = "n", ResourceId = resource.Id };

        Handler(session).Handle(session, Confirm(session, UgcType.GuildBanner, resource.Id));

        List<byte[]> answers = Answers();
        Assert.That(answers, Has.Count.EqualTo(1));
        var reader = new ByteReader(answers[0], 2);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) UgcType.GuildBanner));
        Assert.That(reader.ReadLong(), Is.EqualTo(resource.Id));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ms2/poster.png"));
        Assert.That(session.StagedGuildPoster, Is.Null);
    }

    // The world refuses: the guild error shows, and Ugc 0 follows so that the upload is unlocked.
    [Test]
    public void GuildPoster_RefusedByTheWorld_UnlocksWithUgcZero() {
        GameSession session = Start();
        SetGuild(session);
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        UgcResource resource = Resource(UgcType.GuildBanner, session.Guild.Id, "ms2/poster.png");
        session.StagedGuildPoster = new GuildPoster { Id = 3, OwnerId = session.CharacterId, OwnerName = "n", ResourceId = resource.Id };

        Handler(session).Handle(session, Confirm(session, UgcType.GuildBanner, resource.Id));

        List<byte[]> answers = Answers();
        Assert.That(answers.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Guild, SendOp.Ugc }));
        Assert.That(answers[1].Skip(2).Take(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) UgcType.GuildBanner }));
    }

    // A resource that is not the staged poster's is not made the poster, and the client is still unlocked.
    [Test]
    public void GuildPoster_NotTheStagedUpload_UnlocksWithUgcZero() {
        GameSession session = Start();
        SetGuild(session);
        UgcResource resource = Resource(UgcType.GuildBanner, session.Guild.Id, "ms2/poster.png");

        Handler(session).Handle(session, Confirm(session, UgcType.GuildBanner, resource.Id));

        List<byte[]> answers = Answers();
        Assert.That(answers, Has.Count.EqualTo(1));
        Assert.That(answers[0].Skip(2).Take(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) UgcType.GuildBanner }));
    }

    [Test]
    public void GuildEmblem_NotTheStagedUpload_UnlocksWithUgcZero() {
        GameSession session = Start();
        SetGuild(session);
        UgcResource resource = Resource(UgcType.GuildEmblem, session.Guild.Id, "ms2/emblem.png");

        Handler(session).Handle(session, Confirm(session, UgcType.GuildEmblem, resource.Id));

        List<byte[]> answers = Answers();
        Assert.That(answers, Has.Count.EqualTo(1));
        Assert.That(answers[0].Skip(2).Take(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) UgcType.GuildEmblem }));
    }

    [Test]
    public void UnknownResource_UnlocksWithUgcZero() {
        GameSession session = Start();

        Handler(session).Handle(session, Confirm(session, UgcType.Banner, 987654));

        List<byte[]> answers = Answers();
        Assert.That(answers, Has.Count.EqualTo(1));
        Assert.That(answers[0].Skip(2).Take(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) UgcType.Banner }));
    }

    // A banner confirmation without a paid upload shows nothing; it is answered with Ugc 0 as well.
    [Test]
    public void Banner_WithoutAPaidUpload_UnlocksWithUgcZero() {
        GameSession session = Start();
        UgcResource resource = Resource(UgcType.Banner, 1, "ms2/banner.png");

        Handler(session).Handle(session, Confirm(session, UgcType.Banner, resource.Id));

        List<byte[]> answers = Answers();
        Assert.That(answers, Has.Count.EqualTo(1));
        Assert.That(answers[0].Skip(2).Take(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) UgcType.Banner }));
    }
}
