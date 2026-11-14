using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The guild modes of the member list and the guild window on the channel, in the client writers' order
// (CGuildManager::SendGuildMode8/10/13/15/63/64/68): a World server that cannot be reached is answered with the
// client's "unknown error" text as every other guild mode does, a master change to a name no member has gets
// "member not found", and a message or notice with a banned word is refused before the World is asked.
public class GuildModeHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string BannedWord = "badword";

    private TestGameSession test = null!;
    private GameSession session = null!;
    private GuildHandler handler = null!;
    private GuildMember self = null!;
    private GuildMember other = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Capacity = 10, DonateMax = 3 } }));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.Start();
        session = test.Session;
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        words.BanWordMetadata.Add(new BanWordMetadata(0, BannedWord, false));
        words.SaveChanges();
        handler = new GuildHandler {
            World = test.World,
            TableMetadata = test.TableMetadata,
            BanWordStorage = new BanWordStorage(words),
            ServerTableMetadata = test.ServerTableMetadata,
        };
        Join();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        self?.Dispose();
        other?.Dispose();
    }

    // The session as the master of guild 7 (every right), with one member of rank 2.
    private void Join() {
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        typeof(GuildManager).GetProperty("Properties", Any)!.SetValue(manager, property with { Level = 1, DonateMax = 3 });
        self = Member(session.AccountId, session.CharacterId, "Self", 0);
        other = Member(session.AccountId + 1, session.CharacterId + 1, "Other", 2);
        var guild = new Guild(7, "Guild", self);
        guild.Members[self.CharacterId] = self;
        guild.Members[other.CharacterId] = other;
        guild.Ranks.Add(new GuildRank { Id = 0, Name = "Master", Permission = GuildPermission.All });
        guild.Ranks.Add(new GuildRank { Id = 1, Name = "Jr", Permission = GuildPermission.Default });
        guild.Ranks.Add(new GuildRank { Id = 2, Name = "Member", Permission = GuildPermission.Default });
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    private static GuildMember Member(long accountId, long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(accountId, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        return new GuildMember { GuildId = 7, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private void Send(byte mode, Action<IByteWriter> body) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body(writer);
        }));
    }

    private void WorldDown() {
        test.World.GuildWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));
    }

    private string[] GuildAnswers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string[] Unknown => [Hex(GuildPacket.Error(GuildError.s_guild_err_unknown))];

    // Guild 8 (expel): before, a World error gave no answer at all.
    [Test]
    public void Expel_WorldDown_Unknown() {
        WorldDown();
        Send(8, writer => writer.WriteUnicodeString("Other"));

        Assert.That(test.World.GuildRequests.Single().Expel.ReceiverId, Is.EqualTo(other.CharacterId));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // Guild 10 (a member's rank): a name, then a rank from 1 to the last index.
    [Test]
    public void UpdateMemberRank_WorldDown_Unknown() {
        WorldDown();
        Send(10, writer => {
            writer.WriteUnicodeString("Other");
            writer.WriteByte(1);
        });

        Assert.That(test.World.GuildRequests.Single().UpdateMember.Rank, Is.EqualTo(1));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // Guild 13 (the member's own message).
    [Test]
    public void UpdateMemberMessage_WorldDown_Unknown() {
        WorldDown();
        Send(13, writer => writer.WriteUnicodeString("Hello"));

        Assert.That(test.World.GuildRequests.Single().UpdateMember.Message, Is.EqualTo("Hello"));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // Guild 15 (check-in, no body): a member who joined long ago and has not checked in.
    [Test]
    public void CheckIn_WorldDown_Unknown() {
        WorldDown();
        Send(15, _ => { });

        Assert.That(test.World.GuildRequests.Single().CheckIn.GuildId, Is.EqualTo(7));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // The World took the check-in and told the members (UpdateMember with today's time) but its answer was lost:
    // the player is paid and told, as the World refuses a second check-in today.
    [Test]
    public void CheckIn_AnswerLost_AfterWorldTookIt_CheckedIn() {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        test.World.GuildWorld = _ => {
            session.Guild.UpdateMemberContribution(self.CharacterId, now, 10);
            throw new RpcException(new Status(StatusCode.Unavailable, "answer lost"));
        };
        Send(15, _ => { });

        Assert.That(self.CheckinTime, Is.EqualTo(now));
        string[] answers = GuildAnswers();
        Assert.That(answers, Does.Contain(Hex(GuildPacket.CheckedIn())));
        Assert.That(answers, Does.Not.Contain(Unknown[0]));
    }

    // The member update of a check-in is kept on the game's copy: a second check-in the same day is not sent on.
    [Test]
    public void CheckIn_AfterMemberUpdate_NotSentAgain() {
        session.Guild.UpdateMemberContribution(self.CharacterId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 10);
        test.TakeSent();
        Send(15, _ => { });

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.Empty);
    }

    // Guild 63 (a new master), from the master.
    [Test]
    public void UpdateLeader_WorldDown_Unknown() {
        WorldDown();
        Send(63, writer => writer.WriteUnicodeString("Other"));

        Assert.That(test.World.GuildRequests.Single().UpdateLeader.LeaderId, Is.EqualTo(other.CharacterId));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // Guild 63 with a name no member has: before, nothing at all was answered.
    [Test]
    public void UpdateLeader_NotAMember_NullMember() {
        Send(63, writer => writer.WriteUnicodeString("Stranger"));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_null_member)) }));
    }

    // Guild 64 (the notice): the kind 1, then the text.
    [Test]
    public void UpdateNotice_WorldDown_Unknown() {
        WorldDown();
        Send(64, writer => {
            writer.WriteByte(GuildRules.NoticeKind);
            writer.WriteUnicodeString("Notice");
        });

        Assert.That(test.World.GuildRequests.Single().UpdateNotice.Message, Is.EqualTo("Notice"));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // Guild 68 (listed in the search, then the focus bits).
    [Test]
    public void UpdateFocus_WorldDown_Unknown() {
        WorldDown();
        Send(68, writer => {
            writer.WriteByte(1);
            writer.WriteInt(3);
        });

        Assert.That(test.World.GuildRequests.Single().UpdateFocus.Focus, Is.EqualTo(3));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // A message or a notice with a banned word: the client's forbidden-word text (12), the World not asked and
    // nothing changed. Before, both went to the World and were shown to every member.
    [TestCase("badword")]
    [TestCase("a BadWord here")]
    public void UpdateMemberMessage_Banned_Refused(string text) {
        Send(13, writer => writer.WriteUnicodeString(text));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_name_value)) }));
    }

    [TestCase("badword")]
    [TestCase("a BadWord here")]
    public void UpdateNotice_Banned_Refused(string text) {
        Send(64, writer => {
            writer.WriteByte(GuildRules.NoticeKind);
            writer.WriteUnicodeString(text);
        });

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_name_value)) }));
    }

    // A clean text, and a blank one (clearing the message or the notice), go to the World and are answered.
    [TestCase("Hello")]
    [TestCase("")]
    public void UpdateMemberMessage_Clean_Sent(string text) {
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 7 };
        Send(13, writer => writer.WriteUnicodeString(text));

        Assert.That(test.World.GuildRequests.Single().UpdateMember.Message, Is.EqualTo(text));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.UpdateMemberMessage(text)) }));
    }

    [TestCase("Notice")]
    [TestCase("")]
    public void UpdateNotice_Clean_Sent(string text) {
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 7 };
        Send(64, writer => {
            writer.WriteByte(GuildRules.NoticeKind);
            writer.WriteUnicodeString(text);
        });

        Assert.That(test.World.GuildRequests.Single().UpdateNotice.Message, Is.EqualTo(text));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.UpdateNotice(text)) }));
    }

    // A guild mail with a banned word in the title or the text: the forbidden-word text (12), no mail turn taken
    // and no mail written. Before, the mail went to every member.
    [TestCase("badword", "text")]
    [TestCase("title", "a BadWord here")]
    public void GuildMail_Banned_Refused(string title, string content) {
        Send(71, writer => {
            writer.WriteUnicodeString(title);
            writer.WriteUnicodeString(content);
        });

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_name_value)) }));
    }
}
