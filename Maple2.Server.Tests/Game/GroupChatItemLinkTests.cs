using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// A group chat line's item links: the group messenger keeps its own items (UIGroupMessengerBrowser::ReadItemLinks),
// filled only by GroupChat 0xb, so each member gets the sender's linked items in 0xb before the line (mode 10).
public class GroupChatItemLinkTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChatId = 5;

    private static string Link(long uid) => $"<A HREF=\"event:itemTooltip,{uid}\">[item]</A>";

    // The handler hands the world the uids of the linked items the sender holds, and no other.
    [Test]
    public void Line_CarriesTheSendersLinkedItems() {
        var test = new TestGameSession();
        Item held = test.Store(TestGameSession.Metadata(20000001, type: 2));
        GameSession session = test.Start();
        session.GroupChats ??= new ConcurrentDictionary<int, GroupChatManager>();
        session.GroupChats[ChatId] = (GroupChatManager) RuntimeHelpers.GetUninitializedObject(typeof(GroupChatManager));
        var handler = new GroupChatHandler { World = test.World };

        string message = $"look {Link(held.Uid)} and {Link(held.Uid + 1000)}";
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteUnicodeString(message);
            writer.WriteInt(ChatId);
        }));

        GroupChatRequest request = test.World.GroupChats.Single();
        Assert.Multiple(() => {
            Assert.That(request.GroupChatId, Is.EqualTo(ChatId));
            Assert.That(request.Chat.Message, Is.EqualTo(message));
            Assert.That(request.Chat.ItemUids, Is.EqualTo(new[] { held.Uid }));
        });
    }

    // A live chat restriction refuses a group line too (CGroupChatManager::SendGroupChatMode10), with the client's text.
    [Test]
    public void RestrictedAccount_LineIsRefused() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.GroupChats ??= new ConcurrentDictionary<int, GroupChatManager>();
        session.GroupChats[ChatId] = (GroupChatManager) RuntimeHelpers.GetUninitializedObject(typeof(GroupChatManager));
        session.AccountBlocks = [new AccountBlock { Id = 1, Kind = 6, ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600 }];
        test.TakeSent();
        var handler = new GroupChatHandler { World = test.World };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteUnicodeString("hello");
            writer.WriteInt(ChatId);
        }));

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ChatPacket.Alert(Maple2.Model.Enum.StringCode.s_admin_block_chat).ToArray()) }));
    }

    // A group line with a sticker the sender may not use goes nowhere, with the print failure notice; one it may use
    // goes out (UserChatHandler.RefuseUnusableSticker).
    [TestCase(false)]
    [TestCase(true)]
    public void StickerLine_NeedsAHeldGroup(bool held) {
        var test = new TestGameSession();
        test.SetTable(new ChatStickerTable(new System.Collections.Generic.Dictionary<int, ChatStickerMetadata> { [1010] = new(1010, 3) }));
        GameSession session = test.Start();
        session.GroupChats ??= new ConcurrentDictionary<int, GroupChatManager>();
        session.GroupChats[ChatId] = (GroupChatManager) RuntimeHelpers.GetUninitializedObject(typeof(GroupChatManager));
        if (held) {
            session.Player.Value.Unlock.StickerSets[3] = Maple2.Server.Game.Util.ChatStickerRules.Permanent;
        }
        test.TakeSent();
        var handler = new GroupChatHandler { World = test.World };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteUnicodeString("<img src='img://emoticon:1010' vspace='-6'/>");
            writer.WriteInt(ChatId);
        }));

        Assert.That(test.World.GroupChats, Has.Count.EqualTo(held ? 1 : 0));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), held ? Is.Empty
            : Is.EqualTo(new[] { Convert.ToHexString(ChatStickerPacket.Error(Maple2.Model.Error.ChatStickerError.s_msg_chat_emoticon_print_failed).ToArray()) }));
    }

    // The 0xb block: chat id, sender name, count, then per item its uid, id and rarity and the item block.
    [Test]
    public void ItemLinks_IsReadItemLinksShape() {
        var test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(20000001, type: 2));

        var reader = new ByteReader(GroupChatPacket.ItemLinks(ChatId, "sender", [item]).ToArray());
        Assert.Multiple(() => {
            Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.GroupChat));
            Assert.That(reader.ReadByte(), Is.EqualTo(0xb));
            Assert.That(reader.ReadInt(), Is.EqualTo(ChatId));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("sender"));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(item.Uid));
            Assert.That(reader.ReadInt(), Is.EqualTo(item.Id));
            Assert.That(reader.ReadInt(), Is.EqualTo(item.Rarity));
        });
        var block = new ByteWriter();
        block.WriteClass<Item>(item);
        Assert.That(reader.Available, Is.EqualTo(block.Length));
    }

    // Each member gets 0xb with the items that still exist, then the line; a line without links gets no 0xb.
    [TestCase(true)]
    [TestCase(false)]
    public void Channel_SendsTheItemsBeforeTheLine(bool linked) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        TestGameSession[] tests = [new(options), new(options)];
        Item item = tests[0].Store(TestGameSession.Metadata(20000001, type: 2));
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        foreach (TestGameSession test in tests) {
            GameSession session = test.Start();
            test.TakeSent();
            sessions[session.CharacterId] = session;
        }
        Assume.That(sessions, Has.Count.EqualTo(tests.Length));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        var channel = new ChannelService(server, null!, tests[0].Storage, null!, null!, null!);

        var chat = new GroupChatRequest.Types.Chat { Message = "look", RequesterName = "sender" };
        if (linked) {
            chat.ItemUids.Add(item.Uid);
            chat.ItemUids.Add(item.Uid + 1000);
        }
        var request = new GroupChatRequest { GroupChatId = ChatId, Chat = chat };
        request.ReceiverIds.Add(sessions.Keys);
        channel.GroupChat(request, null!).GetAwaiter().GetResult();

        Item stored;
        using (GameStorage.Request db = tests[0].Storage.Context()) {
            stored = db.GetItem(item.Uid)!;
        }
        string line = Convert.ToHexString(GroupChatPacket.Chat("sender", ChatId, "look").ToArray());
        string[] expected = linked
            ? [Convert.ToHexString(GroupChatPacket.ItemLinks(ChatId, "sender", [stored]).ToArray()), line]
            : [line];
        foreach (TestGameSession test in tests) {
            string[] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GroupChat).Select(Convert.ToHexString).ToArray();
            Assert.That(sent, Is.EqualTo(expected));
        }
    }
}
