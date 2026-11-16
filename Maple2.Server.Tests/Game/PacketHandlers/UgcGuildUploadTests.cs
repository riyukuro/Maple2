using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The guild uploads (Ugc 1): the emblem (kind 6, CUgcGuildMarkProxy::Write: i64 guild id) and the guild house poster
// (kind 8, CUgcGuildPosterProxy::Write: i64 guild id, i32 the poster object's housePosterId). The upload is of the
// sender's own guild; the emblem needs the rank's emblem right, the poster only a poster object of the table. The
// accepted upload opens the web upload (Ugc 2) and stages it for the confirmation. The poster object is made up.
public class UgcGuildUploadTests {
    private const int PosterObject = 90001;
    private const int PosterId = 3;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private UgcHandler handler = null!;
    private Guild guild = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new InteractObjectTable(new Dictionary<int, InteractObjectMetadata> {
            [PosterObject] = new(PosterObject, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: PosterId,
                WeaponItemId: 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
                new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []),
        }));
        test.Start();
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Guild", test.CharacterId)!;
        }
        // The loaded guild comes without its members (GetGuildMembers fills them): the leader, rank 0.
        var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Leader", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        guild.Members[session.CharacterId] = new GuildMember { GuildId = guild.Id, Info = info, Rank = 0, JoinTime = 1000 };
        typeof(GuildManager).GetProperty(nameof(GuildManager.Guild))!.SetValue(session.Guild, guild);
        test.TakeSent();
        handler = new UgcHandler {
            WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                NullLogger<WebStorage>.Instance),
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            World = null!,
        };
    }

    private void Upload(UgcType type, long guildId, int? posterId = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(0);
            writer.WriteByte((byte) type);
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
            writer.WriteLong(guildId);
            if (posterId != null) {
                writer.WriteInt(posterId.Value);
            }
        }));
    }

    private List<byte[]> Sent(SendOp op) => test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).ToList();

    private static void AssertRefused(byte[] reply, UgcType type) {
        Assert.That(reply.Skip(2).ToArray(), Is.EqualTo(new byte[] { 0, (byte) type, (byte) UgcError.Refused }));
    }

    [Test]
    public void Emblem_FromTheLeader_OpensTheWebUpload() {
        Upload(UgcType.GuildEmblem, guild.Id);

        Assert.That(Sent(SendOp.Ugc).Single()[2], Is.EqualTo(2));
        Assert.That(session.StagedGuildEmblemUgc, Is.Not.EqualTo(0));
    }

    // A rank without the emblem right gets the guild's no-authority text, and the upload is unlocked.
    [Test]
    public void Emblem_WithoutTheEmblemRight_IsRefused() {
        guild.Members[session.CharacterId].Rank = 1;

        Upload(UgcType.GuildEmblem, guild.Id);
        List<byte[]> sent = test.TakeSent();

        Assert.Multiple(() => {
            Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Guild), Is.EqualTo(1));
            AssertRefused(sent.Single(packet => TestGameSession.Op(packet) == SendOp.Ugc), UgcType.GuildEmblem);
            Assert.That(session.StagedGuildEmblemUgc, Is.EqualTo(0));
        });
    }

    [Test]
    public void Emblem_OfAnotherGuild_IsRefused() {
        Upload(UgcType.GuildEmblem, guild.Id + 1);

        AssertRefused(Sent(SendOp.Ugc).Single(), UgcType.GuildEmblem);
        Assert.That(session.StagedGuildEmblemUgc, Is.EqualTo(0));
    }

    [Test]
    public void Poster_OnAPosterObject_OpensTheWebUploadAndStagesThePoster() {
        Upload(UgcType.GuildBanner, guild.Id, PosterId);

        Assert.That(Sent(SendOp.Ugc).Single()[2], Is.EqualTo(2));
        Assert.That(session.StagedGuildPoster, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(session.StagedGuildPoster!.Id, Is.EqualTo(PosterId));
            Assert.That(session.StagedGuildPoster.OwnerId, Is.EqualTo(session.CharacterId));
            Assert.That(session.StagedGuildPoster.ResourceId, Is.Not.EqualTo(0));
        });
    }

    // The client tests no rank right for a poster: a member without the emblem right uploads one too.
    [Test]
    public void Poster_FromAPlainMember_IsAccepted() {
        guild.Members[session.CharacterId].Rank = 5;

        Upload(UgcType.GuildBanner, guild.Id, PosterId);

        Assert.That(Sent(SendOp.Ugc).Single()[2], Is.EqualTo(2));
    }

    [TestCase(0)]
    [TestCase(PosterId + 1)]
    public void Poster_NotAPosterObject_IsRefused(int posterId) {
        Upload(UgcType.GuildBanner, guild.Id, posterId);

        AssertRefused(Sent(SendOp.Ugc).Single(), UgcType.GuildBanner);
        Assert.That(session.StagedGuildPoster, Is.Null);
    }

    [Test]
    public void Poster_OfAnotherGuild_IsRefused() {
        Upload(UgcType.GuildBanner, guild.Id + 1, PosterId);

        AssertRefused(Sent(SendOp.Ugc).Single(), UgcType.GuildBanner);
        Assert.That(session.StagedGuildPoster, Is.Null);
    }
}
