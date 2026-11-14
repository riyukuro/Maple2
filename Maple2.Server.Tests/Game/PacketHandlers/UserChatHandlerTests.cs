using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// UserChat from the client: i32 type, text, recipient name, i64 club id. World and channel lines carry the
// sender's channel, which the client prints in front of a world line.
public class UserChatHandlerTests {
    private const short Channel = 7;
    private static readonly FieldInfo ChannelField = typeof(GameServer).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Static)!;

    private TestGameSession test = null!;
    private UserChatHandler handler = null!;
    private short channelBefore;

    [SetUp]
    public void SetUp() {
        channelBefore = GameServer.GetChannel();
        ChannelField.SetValue(null, Channel);
    }

    [TearDown]
    public void TearDown() {
        ChannelField.SetValue(null, channelBefore);
    }

    private GameSession Login(long meret) {
        test = new TestGameSession();
        test.AddMap(TestGameSession.Map(2000023));
        handler = new UserChatHandler { World = test.World, GameStorage = test.Storage, ServerTableMetadata = test.ServerTableMetadata };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        session.Currency.Meret = meret;
        test.TakeSent();
        return session;
    }

    private void Say(GameSession session, ChatType type, string text) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt((int) type);
            writer.WriteUnicodeString(text);
            writer.WriteUnicodeString();
            writer.WriteLong();
        }));
    }

    [Test]
    public void WorldChat_HandsTheSendersChannelToTheWorld() {
        GameSession session = Login(meret: 100000);

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
        ChatRequest request = test.World.Chats[0];
        Assert.That(request.ChatCase, Is.EqualTo(ChatRequest.ChatOneofCase.World));
        Assert.That(request.Channel, Is.EqualTo(Channel));
        Assert.That(request.Message, Is.EqualTo("hello"));
        Assert.That(session.Currency.Meret, Is.EqualTo(100000 - test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat));
    }

    [Test]
    public void WorldChat_WithoutMeretSendsNothingToTheWorld() {
        GameSession session = Login(meret: 0);
        Assume.That(test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat, Is.GreaterThan(0));

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Is.Empty);
    }

    private static RpcException Down() => new(new Status(StatusCode.Unavailable, "down"));

    // A world line the World does not take costs nothing: the merets are taken only for a line that went out.
    [Test]
    public void WorldChat_WithTheWorldDownKeepsTheMerets() {
        GameSession session = Login(meret: 100000);
        Assume.That(test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat, Is.GreaterThan(0));
        test.World.ChatDelivery = _ => throw Down();

        Say(session, ChatType.World, "hello");

        Assert.That(session.Currency.Meret, Is.EqualTo(100000));
    }

    // A session holding one free world chat coupon.
    private (GameSession, Item) LoginWithCoupon(long meret = 0) {
        test = new TestGameSession();
        test.AddMap(TestGameSession.Map(2000023));
        ItemMetadata coupon = TestGameSession.Metadata(20300001);
        Item item = test.Store(coupon with { Property = coupon.Property with { Tag = ItemTag.FreeWorldChatCoupon } });
        handler = new UserChatHandler { World = test.World, GameStorage = test.Storage, ServerTableMetadata = test.ServerTableMetadata };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        session.Currency.Meret = meret;
        test.TakeSent();
        return (session, item);
    }

    // The free world chat coupon is taken only for a line that went out too.
    [Test]
    public void WorldChat_WithTheWorldDownKeepsTheCoupon() {
        (GameSession session, _) = LoginWithCoupon();
        test.World.ChatDelivery = _ => throw Down();

        Say(session, ChatType.World, "hello");

        Assert.That(session.Item.Inventory.Find(20300001), Is.Not.Empty);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.Notice), Is.Empty);

        test.World.ChatDelivery = null;
        Say(session, ChatType.World, "hello");

        Assert.That(session.Item.Inventory.Find(20300001), Is.Empty);
    }

    // An expired coupon is no coupon (Consume refuses it): the line costs the merets, not a free line.
    [Test]
    public void WorldChat_WithAnExpiredCouponCostsTheMerets() {
        (GameSession session, Item coupon) = LoginWithCoupon(meret: 100000);
        Assume.That(test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat, Is.GreaterThan(0));
        session.Item.Inventory.Get(coupon.Uid)!.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meret, Is.EqualTo(100000 - test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat));
    }

    // The inventory is not locked while a paid line waits for the World (the field thread takes the same lock for
    // item skills and rewards), and the call has a deadline.
    [Test]
    public void WorldChat_CouponLineWaitsForTheWorldUnlocked() {
        (GameSession session, _) = LoginWithCoupon();
        bool locked = true;
        test.World.ChatDelivery = _ => locked = Monitor.IsEntered(session.Item);

        Say(session, ChatType.World, "hello");

        Assert.That(locked, Is.False);
        Assert.That(test.World.ChatDeadlines.Single(), Is.Not.Null);
        Assert.That(session.Item.Inventory.Find(20300001), Is.Empty);
    }

    // A sticker in a chat line must be one the player may use (the client's IsEmoticonUsable): in the table, its
    // group held and not run out. Any other line goes nowhere, with the print failure notice.
    private const int StickerId = 1010;
    private const int StickerGroup = 3;
    private const string StickerLine = "hi <img src='img://emoticon:1010' vspace='-6'/>";

    private GameSession LoginWithStickers(long? groupExpiry) {
        GameSession session = Login(meret: 100000);
        test.SetTable(new ChatStickerTable(new Dictionary<int, ChatStickerMetadata> { [StickerId] = new(StickerId, StickerGroup) }));
        if (groupExpiry != null) {
            session.Player.Value.Unlock.StickerSets[StickerGroup] = groupExpiry.Value;
        }
        return session;
    }

    [Test]
    public void StickerLine_WithAHeldGroupGoesOut() {
        GameSession session = LoginWithStickers(ChatStickerRules.Permanent);

        Say(session, ChatType.World, StickerLine);

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
    }

    [TestCase(false, StickerLine)]
    [TestCase(true, StickerLine)]
    [TestCase(true, "hi <IMG SRC='img://emoticon:1011'/>")]
    public void StickerLine_NotUsableIsRefused(bool expired, string line) {
        GameSession session = LoginWithStickers(expired ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60 : null);

        Say(session, ChatType.World, line);

        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(session.Currency.Meret, Is.EqualTo(100000));
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.ChatStamp),
            Is.EqualTo(new[] { Hex(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_print_failed)) }));
    }

    // Typed text that only names a sticker is no tag (the input box escapes a typed "<").
    [Test]
    public void TextNamingASticker_GoesOut() {
        GameSession session = LoginWithStickers(null);

        Say(session, ChatType.World, "img://emoticon:1010 &lt;img src='img://emoticon:1010'");

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
    }

    // Normal chat goes to the field. The int after the GM flag is the channel in a field that is no instance and 0
    // in an instanced one.
    [TestCase(InstanceType.none, Channel)]
    [TestCase(InstanceType.solo, 0)]
    [TestCase(InstanceType.ugcMap, 0)]
    public void NormalChat_GoesToTheFieldWithTheChannelOutsideInstances(InstanceType instance, int expected) {
        GameSession session = Login(meret: 0);
        typeof(FieldManager).GetProperty("FieldInstance")!.SetValue(session.Field,
            new InstanceFieldMetadata(0, instance, 0, false, 0, true, 0, 0, 0, 0));

        Say(session, ChatType.Normal, "hi");

        byte[] line = test.TakeSent().Single(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.UserChat);
        int flag = 2 + 8 + 8 + (2 + session.PlayerName.Length * 2) + 1 + (2 + 2 * 2) + 4;
        Assert.That(BitConverter.ToInt32(line, flag - 4), Is.EqualTo((int) ChatType.Normal));
        Assert.That(BitConverter.ToInt32(line, flag + 1), Is.EqualTo(expected));
        Assert.That(test.World.Chats, Is.Empty);
    }

    // Party and guild lines hand the same channel int to the world.
    [TestCase(InstanceType.none, Channel)]
    [TestCase(InstanceType.solo, 0)]
    public void LineChannel_IsZeroInAnInstance(InstanceType instance, int expected) {
        GameSession session = Login(meret: 0);
        typeof(FieldManager).GetProperty("FieldInstance")!.SetValue(session.Field,
            new InstanceFieldMetadata(0, instance, 0, false, 0, true, 0, 0, 0, 0));

        Assert.That(UserChatHandler.LineChannel(session), Is.EqualTo(expected));
    }

    // The received whisper carries the sender's channel after the GM flag; the sender's own copy keeps 0.
    [Test]
    public void Whisper_CarriesTheChannelInTheReceivedLineOnly() {
        byte[] received = ChatPacket.Whisper(1, 2, "ab", "hi", "pic", channel: Channel).ToArray();
        byte[] plain = ChatPacket.Whisper(1, 2, "ab", "hi", "pic").ToArray();
        // op 2, ids 16, name 2 + 4, flag 1, text 2 + 4, type 4, gm 1: the int starts at 36.
        Assert.That(BitConverter.ToInt32(received, 36), Is.EqualTo(Channel));
        Assert.That(BitConverter.ToInt32(plain, 36), Is.EqualTo(0));
        Assert.That(ChatPacket.Whisper(0, 2, "ab", "hi", channel: Channel).ToArray(),
            Is.EqualTo(ChatPacket.Whisper(0, 2, "ab", "hi").ToArray()));
    }

    // A rock-paper-scissors line (type 23) goes back to the field as type 23 with channel 0 and nothing to the world.
    [Test]
    public void MicroGameLine_GoesToTheFieldWithItsType() {
        GameSession session = Login(meret: 0);

        Say(session, (ChatType) 23, "hi");

        byte[][] lines = test.TakeSent().Where(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.UserChat).ToArray();
        Assert.That(lines, Has.Length.EqualTo(1));
        Assert.That(lines[0], Is.EqualTo(ChatPacket.Message(session.AccountId, session.CharacterId, session.PlayerName, ChatType.MicroGame, "hi").ToArray()));
        // op 2, ids 16, name 2 + 2n, flag 1, text 2 + 4: the type, then gm 1 and the channel.
        int type = 2 + 16 + 2 + 2 * session.PlayerName.Length + 1 + 2 + 4;
        Assert.That(BitConverter.ToInt32(lines[0], type), Is.EqualTo(23));
        Assert.That(BitConverter.ToInt32(lines[0], type + 5), Is.EqualTo(0));
        Assert.That(test.World.Chats, Is.Empty);
    }

    // Party, guild and system notice lines carry the channel the request names.
    [Test]
    public void Message_WritesTheChannelOfTheRequest() {
        foreach (ChatType type in new[] { ChatType.Party, ChatType.Guild, ChatType.SystemNotice }) {
            byte[] line = ChatPacket.Message(1, 2, "ab", type, "hi", channel: Channel).ToArray();
            Assert.That(BitConverter.ToInt32(line, 36), Is.EqualTo(Channel), type.ToString());
        }
    }
    // A guild with the session's character in rank 0, which has the given permissions.
    private static void Join(GameSession session, GuildPermission permission) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", any)!.SetValue(manager, Log.Logger);
        var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, session.PlayerName, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        var self = new GuildMember { GuildId = 7, Info = info, Rank = 0, JoinTime = 1000 };
        var guild = new Guild(7, "Guild", self);
        guild.Members[self.CharacterId] = self;
        guild.Ranks.Add(new GuildRank { Id = 0, Name = "rank", Permission = permission });
        typeof(GuildManager).GetProperty("Guild", any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    // The guild megaphone (kind 18) goes to the guild with channel 0 and the notice flag; the client sends it only
    // with the rank permission 0x800 (SendAlert), and the server holds to that too.
    [TestCase(GuildPermission.Default | GuildPermission.SendAlert, true)]
    [TestCase(GuildPermission.Default, false)]
    public void GuildNotice_NeedsTheAlertPermission(GuildPermission permission, bool sent) {
        GameSession session = Login(meret: 0);
        Join(session, permission);

        Say(session, ChatType.GuildNotice, "all here");

        if (!sent) {
            Assert.That(test.World.Chats, Is.Empty);
            return;
        }
        ChatRequest request = test.World.Chats.Single();
        Assert.That(request.Guild.GuildId, Is.EqualTo(7));
        Assert.That(request.Guild.Notice, Is.True);
        Assert.That(request.Channel, Is.EqualTo(0));
        Assert.That(request.Message, Is.EqualTo("all here"));
    }

    // Plain guild chat needs no permission and is no notice.
    [Test]
    public void GuildChat_IsNoNotice() {
        GameSession session = Login(meret: 0);
        Join(session, GuildPermission.Default);

        Say(session, ChatType.Guild, "hi");

        ChatRequest request = test.World.Chats.Single();
        Assert.That(request.Guild.Notice, Is.False);
        Assert.That(request.Channel, Is.EqualTo(UserChatHandler.LineChannel(session)));
    }

    // Without a guild nothing goes to the world.
    [Test]
    public void GuildNotice_WithoutAGuildSendsNothing() {
        GameSession session = Login(meret: 0);

        Say(session, ChatType.GuildNotice, "all here");

        Assert.That(test.World.Chats, Is.Empty);
    }

    private static string[] Lines(TestGameSession test, Maple2.Server.Core.Constants.SendOp op) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).Select(Convert.ToHexString).ToArray();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // Kind 22 is the home OX quiz line, which only the server sends (texttohtml.cpp:575): one from a client goes
    // nowhere.
    [Test]
    public void OxQuizLine_FromTheClientSendsNothing() {
        GameSession session = Login(meret: 0);

        Say(session, ChatType.UgcEvent, "o");

        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat), Is.Empty);
        Assert.That(test.World.Chats, Is.Empty);
    }

    // A line longer than the client's input box takes (MaxChatLength) is dropped.
    [Test]
    public void LineLongerThanMaxChatLength_IsDropped() {
        GameSession session = Login(meret: 100000);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MaxChatLength = 5 });

        Say(session, ChatType.World, "abcdef");
        Assert.That(test.World.Chats, Is.Empty);

        Say(session, ChatType.World, "abcde");
        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
    }

    // The party and chaos DPS reports are party lines the client builds itself, well past the input box limit; mini
    // game lines come from a string key. Neither is held to MaxChatLength.
    [Test]
    public void ClientBuiltLines_PassMaxChatLength() {
        GameSession session = Login(meret: 0);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MaxChatLength = 5 });
        var manager = new Maple2.Server.Game.Manager.PartyManager(test.World, session);
        typeof(Maple2.Server.Game.Manager.PartyManager).GetProperty("Party")!
            .SetValue(manager, new Maple2.Model.Game.Party.Party(31, session.AccountId, session.CharacterId, session.PlayerName));
        session.Party = manager;
        string report = "[00:42] 1. " + session.PlayerName + " 123456 2. other 654321 total 01:30";

        Say(session, ChatType.Party, report);
        Say(session, ChatType.MicroGame, "abcdefgh");

        ChatRequest request = test.World.Chats.Single();
        Assert.That(request.Message, Is.EqualTo(report));
        Assert.That(request.Party.PartyId, Is.EqualTo(31));
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat), Has.Length.EqualTo(1));
        Assert.That(ChatRestriction.LimitsLength(ChatType.Normal), Is.True);
        Assert.That(ChatRestriction.LimitsLength(ChatType.Guild), Is.True);
        Assert.That(ChatRestriction.LimitsLength(ChatType.WhisperReject), Is.False);
    }

    // The client-built kinds still have a cap, ChatRestriction.ClientBuiltMaxLength with the html counted: a party
    // line past it, or a mini game line, goes nowhere; one at the cap goes out.
    [Test]
    public void ClientBuiltLines_PastTheHardCap_AreDropped() {
        GameSession session = Login(meret: 0);
        var manager = new Maple2.Server.Game.Manager.PartyManager(test.World, session);
        typeof(Maple2.Server.Game.Manager.PartyManager).GetProperty("Party")!
            .SetValue(manager, new Maple2.Model.Game.Party.Party(31, session.AccountId, session.CharacterId, session.PlayerName));
        session.Party = manager;
        string atCap = new string('a', ChatRestriction.ClientBuiltMaxLength);

        Say(session, ChatType.Party, atCap + "a");
        Say(session, ChatType.MicroGame, "<b>" + atCap + "</b>");
        Say(session, ChatType.WhisperReject, atCap + "a");
        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat), Is.Empty);

        Say(session, ChatType.Party, atCap);
        Assert.That(test.World.Chats.Single().Message, Is.EqualTo(atCap));
    }

    // The fifth equal line within SameChat_CheckTime goes nowhere; the sender gets s_chat_restrict_samechat with
    // the seconds.
    [Test]
    public void FifthEqualLine_SendsTheRestrictNotice() {
        GameSession session = Login(meret: 0);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            SameChat_CheckTime = 60000,
            SameChat_CheckCount = 5,
            SameChat_RestrictTime = 10000,
        });

        for (int i = 0; i < 5; i++) {
            Say(session, ChatType.Normal, "hi");
        }

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.UserChat), Is.EqualTo(4));
        Assert.That(sent.Where(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.Notice).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(Maple2.Server.Core.Packets.NoticePacket.Message(new InterfaceText(StringCode.s_chat_restrict_samechat, "10"))) }));
    }

    private const int MegaphoneId = 20800100;

    // A session with one megaphone item of the function, selected as SuperChat Select keeps it.
    private GameSession LoginWithMegaphone(ItemFunction function) {
        test = new TestGameSession();
        test.AddMap(TestGameSession.Map(2000023));
        test.Store(TestGameSession.Metadata(MegaphoneId, function: new ItemMetadataFunction(function, function.ToString(), "", false)));
        handler = new UserChatHandler { World = test.World, GameStorage = test.Storage, ServerTableMetadata = test.ServerTableMetadata };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        session.Marriage.WeddingHall = WeddingHall.Default;
        session.SuperChatId = MegaphoneId;
        session.SuperChatItemId = MegaphoneId;
        test.TakeSent();
        return session;
    }

    private const string OtherName = "partner";
    private const long PartnerId = 9001;

    // The engagement as storage loads it for the session (GetMarriage): Partner1 is the other person and Partner2 the
    // session's own character. The hall keeps the fixed roles: the reserver and the partner.
    private static void Reserve(GameSession session, long ceremonyTime, bool senderReserved = true) {
        session.Marriage.Marriage = new Marriage {
            Id = 77,
            Status = MaritalStatus.Engaged,
            Partner1 = new MarriagePartner { Info = new PlayerInfo(new CharacterInfo(PartnerId, PartnerId, OtherName, "", "", Gender.Female, Job.Newbie, 20), "", new AchievementInfo(), []) },
            Partner2 = new MarriagePartner { Info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, session.PlayerName, "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []) },
        };
        session.Marriage.WeddingHall = new WeddingHall {
            Id = 9,
            MarriageId = 77,
            ReserverAccountId = senderReserved ? session.AccountId : PartnerId,
            ReserverCharacterId = senderReserved ? session.CharacterId : PartnerId,
            ReserverName = senderReserved ? session.PlayerName : OtherName,
            PartnerName = senderReserved ? OtherName : session.PlayerName,
            CeremonyTime = ceremonyTime,
            PackageId = 3,
            PackageHallId = 4,
        };
    }

    // The wedding megaphone (kind 25) takes the item and hands the reservation to the world with the item id.
    // The groom is the hall's reserver and the bride the partner, whichever of the two sends the line.
    [TestCase(true)]
    [TestCase(false)]
    public void WeddingLine_CarriesTheReservationAndTakesTheItem(bool senderReserved) {
        GameSession session = LoginWithMegaphone(ItemFunction.WeddingChat);
        long ceremony = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        Reserve(session, ceremony, senderReserved);

        Say(session, ChatType.Wedding, "come");

        ChatRequest request = test.World.Chats.Single();
        Assert.That(request.ChatCase, Is.EqualTo(ChatRequest.ChatOneofCase.Wedding));
        Assert.That(request.Message, Is.EqualTo("come"));
        Assert.That(request.Channel, Is.EqualTo(Channel));
        Assert.That(request.Wedding.ItemId, Is.EqualTo(MegaphoneId));
        Assert.That(request.Wedding.MarriageId, Is.EqualTo(77));
        Assert.That(request.Wedding.PackageId, Is.EqualTo(3));
        Assert.That(request.Wedding.HallId, Is.EqualTo(4));
        Assert.That(request.Wedding.CeremonyTime, Is.EqualTo(ceremony));
        Assert.That(request.Wedding.Groom, Is.EqualTo(senderReserved ? session.PlayerName : OtherName));
        Assert.That(request.Wedding.Bride, Is.EqualTo(senderReserved ? OtherName : session.PlayerName));
        Assert.That(session.Item.Inventory.Find(MegaphoneId), Is.Empty);
        Assert.That(session.SuperChatId, Is.EqualTo(0));
    }

    // A megaphone line the World does not take keeps the item and its selection.
    [TestCase(ItemFunction.SuperWorldChat, ChatType.Super)]
    [TestCase(ItemFunction.WeddingChat, ChatType.Wedding)]
    public void MegaphoneLine_WithTheWorldDownKeepsTheItem(ItemFunction function, ChatType type) {
        GameSession session = LoginWithMegaphone(function);
        Reserve(session, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);
        test.World.ChatDelivery = _ => throw Down();

        Say(session, type, "come");

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
        Assert.That(session.Item.Inventory.Find(MegaphoneId), Is.Not.Empty);
        Assert.That(session.SuperChatId, Is.EqualTo(MegaphoneId));
    }

    [TestCase(ItemFunction.SuperWorldChat, ChatType.Super)]
    [TestCase(ItemFunction.WeddingChat, ChatType.Wedding)]
    public void MegaphoneLine_WaitsForTheWorldUnlocked(ItemFunction function, ChatType type) {
        GameSession session = LoginWithMegaphone(function);
        Reserve(session, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);
        bool locked = true;
        test.World.ChatDelivery = _ => locked = Monitor.IsEntered(session.Item);

        Say(session, type, "come");

        Assert.That(locked, Is.False);
        Assert.That(test.World.ChatDeadlines.Single(), Is.Not.Null);
        Assert.That(session.Item.Inventory.Find(MegaphoneId), Is.Empty);
    }

    // Without a reservation the line goes nowhere, the item stays and the sender gets the client's text.
    [Test]
    public void WeddingLine_WithoutAReservationKeepsTheItem() {
        GameSession session = LoginWithMegaphone(ItemFunction.WeddingChat);

        Say(session, ChatType.Wedding, "come");

        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat),
            Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_err_wedding_chat_cannot_use_no_reservation)) }));
        Assert.That(session.Item.Inventory.Find(MegaphoneId), Is.Not.Empty);
        Assert.That(session.SuperChatId, Is.EqualTo(MegaphoneId));
    }

    [Test]
    public void WeddingLine_AfterTheCeremonyKeepsTheItem() {
        GameSession session = LoginWithMegaphone(ItemFunction.WeddingChat);
        Reserve(session, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);

        Say(session, ChatType.Wedding, "come");

        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat),
            Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_err_wedding_chat_cannot_use_complete)) }));
    }

    // Each megaphone line takes its own item: super chat with a wedding megaphone selected, and the other way
    // round, is refused and the selection dropped.
    [TestCase(ItemFunction.WeddingChat, ChatType.Super)]
    [TestCase(ItemFunction.SuperWorldChat, ChatType.Wedding)]
    public void MegaphoneLine_WithTheOtherItemIsRefused(ItemFunction function, ChatType type) {
        GameSession session = LoginWithMegaphone(function);
        Reserve(session, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);

        Say(session, type, "come");

        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat),
            Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_err_invalid_item)) }));
        Assert.That(session.Item.Inventory.Find(MegaphoneId), Is.Not.Empty);
        Assert.That(session.SuperChatId, Is.EqualTo(0));
    }

    // A live chat restriction (AdminBlock kind 2 Penalty or 6) refuses the line with the client's text; an ended one
    // does not, and the client's own whisper reject still goes out.
    [TestCase(2, StringCode.s_admin_block_velma_notice_chat)]
    [TestCase(6, StringCode.s_admin_block_chat)]
    public void RestrictedAccount_LineIsRefused(int kind, StringCode text) {
        GameSession session = Login(meret: 100000);
        session.AccountBlocks = [Block(kind, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600)];

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat), Is.EqualTo(new[] { Hex(ChatPacket.Alert(text)) }));
    }

    [Test]
    public void EndedRestriction_LineGoesOut() {
        GameSession session = Login(meret: 100000);
        session.AccountBlocks = [Block(6, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1)];

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
    }

    [Test]
    public void RestrictedAccount_WhisperRejectGoesOut() {
        GameSession session = Login(meret: 0);
        session.AccountBlocks = [Block(6, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600)];
        session.WhisperedBy["friend"] = 42;

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt((int) ChatType.WhisperReject);
            writer.WriteUnicodeString();
            writer.WriteUnicodeString("friend");
            writer.WriteLong();
        }));

        Assert.That(test.World.Chats.Single().Whisper.Reject, Is.True);
        Assert.That(Lines(test, Maple2.Server.Core.Constants.SendOp.UserChat), Is.Empty);
    }

    private static AccountBlock Block(int kind, long expiry) => new() { Id = 1, Kind = kind, ExpiryTime = expiry };

    // A whisper to a name no character has: the lookup gives id 0, which is a plain not-found (the whisper error
    // alert) and not a request the world refuses, so nothing is asked of the world and no error is logged.
    [Test]
    public void WhisperToAnUnknownName_IsNotFoundWithoutAWorldRequest() {
        GameSession session = Login(meret: 0);

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt((int) ChatType.WhisperTo);
            writer.WriteUnicodeString("hi");
            writer.WriteUnicodeString("nobody");
            writer.WriteLong();
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ChatPacket.Alert(StringCode.s_whisper_err_target).ToArray()) }));
        Assert.That(test.World.PlayerInfoAsked, Is.Empty);
        Assert.That(test.World.Chats, Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // The storage itself: id 0 is not found, the world is not asked, and nothing at Error level is logged.
    [Test]
    public void PlayerInfoOfIdZero_IsNotFoundWithoutAnError() {
        test = new TestGameSession();
        var errors = new List<string>();
        ILogger before = Log.Logger;
        try {
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(new ErrorLines(errors)).CreateLogger();
            var storage = new Maple2.Server.Game.Util.Sync.PlayerInfoStorage(test.World);

            Assert.That(storage.GetOrFetch(0, out PlayerInfo? info), Is.False);
            Assert.That(info, Is.Null);
        } finally {
            Log.Logger = before;
        }
        Assert.That(test.World.PlayerInfoAsked, Is.Empty);
        Assert.That(errors, Is.Empty);
    }

    private sealed class ErrorLines(List<string> errors) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) {
            if (logEvent.Level >= Serilog.Events.LogEventLevel.Error) {
                errors.Add(logEvent.RenderMessage());
            }
        }
    }
}
