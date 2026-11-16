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
    private const int EventId = 3;
    private const int EventMap = 2000062;

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
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Capacity = 10, DonateMax = 3 } },
            Events: new Dictionary<int, GuildTable.Event> { [EventId] = new(EventId, EventMap) }));
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

    // Guild 67 (a rank): the window sends every changed rank with its name. A name that is the rank's stored one
    // is not checked again, so a rank that keeps a long name from before the rule (here 10 characters, over the
    // KR maximum of 7) still gets new rights; a changed name over the region's maximum is refused.
    private void SendRank(byte id, string name, GuildPermission permission) {
        Send(67, writer => {
            writer.WriteByte(id);
            new GuildRank { Id = id, Name = name, Permission = permission }.WriteTo(writer);
        });
    }

    [Test]
    public void UpdateRank_RightsOnlyOnAStoredLongName_GoesToTheWorld() {
        session.Guild.Guild!.Ranks[1].Name = "Jr. Master";
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 7 };
        SendRank(1, "Jr. Master", GuildPermission.Default | GuildPermission.InviteMembers);

        Assert.That(test.World.GuildRequests.Single().UpdateRank.Name, Is.EqualTo("Jr. Master"));
    }

    // A rank still named the data's default is shown as the client's default; a rights-only edit sends that shown name
    // back, and the World gets the data's default, so the stored row keeps it. A typed name goes as typed.
    [Test]
    public void UpdateRank_RightsOnlyOnAShownClientDefault_StoresTheDataDefault() {
        IReadOnlyList<string> defaults = GuildRules.DefaultRankNames(Target.LOCALE);
        test.SetTable(new GuildRankNameTable(["C0", "C1", "C2", "C3", "C4", "C5"]));
        session.Guild.Guild!.Ranks[1].Name = "C1";
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 7 };
        SendRank(1, "C1", GuildPermission.Default | GuildPermission.InviteMembers);
        SendRank(1, "Elder", GuildPermission.Default);

        Assert.That(test.World.GuildRequests.Select(request => request.UpdateRank.Name), Is.EqualTo(new[] { defaults[1], "Elder" }));
    }

    // The real defaults: every rank stored under the data's default (as the World and the db hold it) or as the
    // session shows it, and the window sending the English client defaults with new rights. On KR data the English
    // names (up to 14 characters) are over the KR maximum of 7, but they are no new names: each goes to the World
    // as the data's default.
    [TestCase(false)]
    [TestCase(true)]
    public void UpdateRank_RightsOnlyOnEveryEnglishDefault_GoesToTheWorld(bool shownInSession) {
        IReadOnlyList<string> defaults = GuildRules.DefaultRankNames(Target.LOCALE);
        string[] english = ["Leader", "Co-Leader", "Guild Member", "Initiate", "Guild Member 2", "Initiate 2"];
        var table = new GuildRankNameTable(english);
        test.SetTable(table);
        Guild guild = session.Guild.Guild!;
        guild.Ranks.Clear();
        for (byte i = 0; i < 6; i++) {
            string stored = shownInSession ? table.Shown(i, defaults[i], defaults) : defaults[i];
            guild.Ranks.Add(new GuildRank { Id = i, Name = stored, Permission = GuildPermission.Default });
        }
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 7 };
        for (byte i = 0; i < 6; i++) {
            SendRank(i, english[i], GuildPermission.Default | GuildPermission.InviteMembers);
        }

        Assert.That(GuildAnswers(), Has.None.EqualTo(Hex(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_data))));
        Assert.That(test.World.GuildRequests.Select(request => request.UpdateRank.Name), Is.EqualTo(defaults));
    }

    [Test]
    public void UpdateRank_NewNameOverTheMaximum_Refused() {
        SendRank(1, "A rank name of 16", GuildPermission.Default);

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_data)) }));
    }

    [Test]
    public void UpdateRank_NewBannedName_Refused() {
        SendRank(1, BannedWord, GuildPermission.Default);

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_data)) }));
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
        // The World's broadcast (22) tells the sender; 11 would be read by the client as a member name.
        Assert.That(GuildAnswers(), Is.Empty);
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

    private void SendMail(string title, string content) {
        Send(71, writer => {
            writer.WriteUnicodeString(title);
            writer.WriteUnicodeString(content);
        });
    }

    private IList<Mail> MailOf(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(characterId).ToList();
    }

    // Guild 71 accepted: the World gives the mail turn, every other member gets a guild mail from the sender (not
    // the sender), the new-mail notice goes out, and the reply is 71 with no body ("mail sent").
    [Test]
    public void GuildMail_Sent_ToEveryOtherMember() {
        test.World.GuildWorld = _ => new GuildResponse();

        SendMail("Title", "Text");

        Assert.That(test.World.GuildRequests.Single().TakeMailTurn.GuildId, Is.EqualTo(7));
        Mail mail = MailOf(other.CharacterId).Single();
        Assert.Multiple(() => {
            Assert.That((mail.Type, mail.SenderId, mail.SenderName, mail.Title, mail.Content),
                Is.EqualTo((MailType.Guild, session.CharacterId, session.PlayerName, "Title", "Text")));
            Assert.That(MailOf(session.CharacterId), Is.Empty);
            Assert.That(test.World.MailNotifications.Select(notice => (notice.CharacterId, notice.MailId)),
                Is.EqualTo(new[] { (other.CharacterId, mail.Id) }));
            Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.SendMail()) }));
        });
    }

    // A rank without the mail right (128): no_authority before the World is asked.
    [Test]
    public void GuildMail_WithoutTheRight_NoAuthority() {
        self.Rank = 2;

        SendMail("Title", "Text");

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(MailOf(other.CharacterId), Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_no_authority)) }));
    }

    // A title of 0 or 101 characters, a text of 0 or 2001 (the client's own limits, CGuildManager::SendGuildMail).
    [TestCase(0, 4)]
    [TestCase(101, 4)]
    [TestCase(5, 0)]
    [TestCase(5, 2001)]
    public void GuildMail_OutOfLength_Unknown(int titleLength, int contentLength) {
        SendMail(new string('t', titleLength), new string('c', contentLength));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));
    }

    // The World refuses the turn (a second mail inside the flood guard) or cannot be reached: its error, no mail.
    [Test]
    public void GuildMail_TurnRefusedOrWorldDown_NoMail() {
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildRules.MailCooldownError };
        SendMail("Title", "Text");
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildRules.MailCooldownError)) }));

        WorldDown();
        SendMail("Title", "Text");
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));

        Assert.That(MailOf(other.CharacterId), Is.Empty);
        Assert.That(test.World.MailNotifications, Is.Empty);
    }

    // Guild 114 (CGuildManager::FUN_1413bb000): the NPC kind goes to the World, which checks and pays; its refusal
    // or a World down is the error line, an upgrade has no answer of its own (the members get 51 and 59).
    [Test]
    public void UpgradeNpc_AsksTheWorld_AndShowsItsRefusal() {
        test.World.GuildWorld = _ => new GuildResponse();
        Send(114, writer => writer.WriteInt((int) GuildNpcType.Goods));
        Assert.That(GuildAnswers(), Is.Empty);

        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_not_enough_guild_fund };
        Send(114, writer => writer.WriteInt((int) GuildNpcType.Goods));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_not_enough_guild_fund)) }));

        WorldDown();
        Send(114, writer => writer.WriteInt((int) GuildNpcType.Goods));
        Assert.That(GuildAnswers(), Is.EqualTo(Unknown));

        Assert.That(test.World.GuildRequests.Select(request => (request.UpgradeNpc.GuildId, request.UpgradeNpc.Type)),
            Is.EqualTo(Enumerable.Repeat((7L, (int) GuildNpcType.Goods), 3)));
    }

    // Guild 100 (CGuildManager::SendGuildMode100, guildmanager.cpp:881-898: byte 100, int event id): an open event
    // of the guild sends the member to the event's map, in the guild's own copy of it.
    [Test]
    public void EnterEvent_Open_MovesToTheEventMapOfTheGuild() {
        session.Guild.Guild!.Events.Add(new GuildEvent(EventId, (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Send(100, writer => writer.WriteInt(EventId));

        MigrateOutRequest move = test.World.Moves.Single();
        Assert.Multiple(() => {
            Assert.That(move.MapId, Is.EqualTo(EventMap));
            Assert.That(move.OwnerId, Is.EqualTo(7L));
        });
    }

    // An event the table has no row for, one the guild has not opened, one whose open time has passed, and the
    // event's own map: no move.
    [Test]
    public void EnterEvent_NotOpenOrNoRow_NoMove() {
        int now = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Guild.Guild!.Events.Add(new GuildEvent(9, now));
        Send(100, writer => writer.WriteInt(9));
        Send(100, writer => writer.WriteInt(EventId));
        session.Guild.Guild!.Events.Add(new GuildEvent(EventId, now - GuildRules.EventOpenSeconds - 1));
        Send(100, writer => writer.WriteInt(EventId));

        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void EnterEvent_OnTheEventMap_NoMove() {
        test.EnterMap(TestGameSession.Map(EventMap));
        session.Guild.Guild!.Events.Add(new GuildEvent(EventId, (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Send(100, writer => writer.WriteInt(EventId));

        Assert.That(test.World.Moves, Is.Empty);
    }

    // Guild 109 (CGuildManager::FUN_1413bb490, guildmanager.cpp:1086-1088): the item as ItemEntity::Write puts it
    // (commonstruct_wa.cpp:84: int id, short rank, int count, 4 bools), then the receiver's name.
    private void SendGift(int itemId, short rank, int count, string receiver) {
        Send(109, writer => {
            writer.WriteInt(itemId);
            writer.WriteShort(rank);
            writer.WriteInt(count);
            writer.WriteBool(false);
            writer.WriteBool(false);
            writer.WriteBool(false);
            writer.WriteBool(false);
            writer.WriteUnicodeString(receiver);
        });
    }

    // The World is asked with the item and the name; the answer (OnPacketStorageGiftResult) is the result byte,
    // with the receiver's name on success.
    [Test]
    public void StorageGift_AsksTheWorld_AndAnswersWithTheReceiver() {
        test.World.GuildWorld = _ => new GuildResponse { ReceiverName = "Other" };
        SendGift(20000001, 3, 2, "Other");

        GuildRequest.Types.StorageGift gift = test.World.GuildRequests.Single().StorageGift;
        Assert.Multiple(() => {
            Assert.That((gift.GuildId, gift.ItemId, gift.Rarity, gift.Amount, gift.ReceiverName),
                Is.EqualTo((7L, 20000001, 3, 2, "Other")));
            Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.StorageGift(GuildError.none, "Other")) }));
        });
    }

    [Test]
    public void StorageGift_Refused_AnswersTheError() {
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_no_master };
        SendGift(20000001, 1, 1, "Nobody");

        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.StorageGift(GuildError.s_guild_err_no_master)) }));
    }

    [Test]
    public void StorageGift_WorldDown_Unknown() {
        WorldDown();
        SendGift(20000001, 1, 1, "Other");

        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.StorageGift(GuildError.s_guild_err_unknown)) }));
    }

    // The client's reader drops 109 outside a guild; the server neither asks nor answers.
    [Test]
    public void StorageGift_OutsideAGuild_Nothing() {
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(session.Guild, null);
        SendGift(20000001, 1, 1, "Other");

        Assert.Multiple(() => {
            Assert.That(test.World.GuildRequests, Is.Empty);
            Assert.That(GuildAnswers(), Is.Empty);
        });
    }
}
