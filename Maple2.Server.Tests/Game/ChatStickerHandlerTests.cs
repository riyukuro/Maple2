using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The chat sticker modes (ChatSticker 1, 3, 4, 5, 6) and their ChatStamp answers. A sticker is usable while its
// group is held and its time has not run out (the client's IsEmoticonUsable); a use of another sticker gets the
// print failure (ChatStamp 7, result 0), a favourite of one gets no answer, and an expiry the client asks for
// only takes the groups whose time has run out by the server's clock.
public class ChatStickerHandlerTests {
    private const int HeldGroup = 1;
    private const int ExpiredGroup = 2;
    private const int OtherGroup = 3;
    private const int HeldSticker = 101;
    private const int ExpiredSticker = 201;
    private const int OtherSticker = 301;

    private static (TestGameSession Test, GameSession Session, ChatStickerHandler Handler) Setup() {
        var test = new TestGameSession();
        test.SetTable(new ChatStickerTable(new Dictionary<int, ChatStickerMetadata> {
            [HeldSticker] = new(HeldSticker, HeldGroup),
            [HeldSticker + 1] = new(HeldSticker + 1, HeldGroup),
            [ExpiredSticker] = new(ExpiredSticker, ExpiredGroup),
            [OtherSticker] = new(OtherSticker, OtherGroup),
        }));
        GameSession session = test.Start();
        // The test session's config is not loaded from a row; the favourites start empty, as a new character's.
        typeof(ConfigManager).GetField("favoriteStickers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, new List<int>());
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Player.Value.Unlock.StickerSets[HeldGroup] = ChatStickerRules.Permanent;
        session.Player.Value.Unlock.StickerSets[ExpiredGroup] = now - 60;
        test.TakeSent();
        return (test, session, new ChatStickerHandler { TableMetadata = test.TableMetadata });
    }

    private static ByteReader Reply(byte[] packet, byte mode) {
        var reader = new ByteReader(packet);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChatStamp));
            Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        });
        return reader;
    }

    private static void AssertPrintFailed(List<byte[]> sent) {
        Assert.That(sent, Has.Count.EqualTo(1));
        ByteReader reader = Reply(sent[0], 7);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    private static ByteReader Use(int stickerId, string window = "messenger", byte chatType = 4) => TestGameSession.Packet(writer => {
        writer.WriteByte(3);
        writer.WriteInt(stickerId);
        writer.WriteUnicodeString(window);
        writer.WriteByte(chatType);
    });

    private static ByteReader Single(byte mode, int stickerId) => TestGameSession.Packet(writer => {
        writer.WriteByte(mode);
        writer.WriteInt(stickerId);
    });

    [Test]
    public void Use_OfAHeldSticker_HandsBackTheWindowAndChatType() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, Use(HeldSticker, "whisper:other", 3));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        ByteReader reader = Reply(sent[0], 3);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(HeldSticker));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("whisper:other"));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [TestCase(ExpiredSticker)]
    [TestCase(OtherSticker)]
    [TestCase(999999)]
    public void Use_OfAStickerNotUsable_IsThePrintFailure(int stickerId) {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, Use(stickerId));

        AssertPrintFailed(test.TakeSent());
    }

    [Test]
    public void GroupChat_OfAHeldSticker_HandsBackTheChatName() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(HeldSticker);
            writer.WriteUnicodeString("group1");
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        ByteReader reader = Reply(sent[0], 4);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(HeldSticker));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("group1"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void GroupChat_OfAnExpiredSticker_IsThePrintFailure() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(ExpiredSticker);
            writer.WriteUnicodeString("group1");
        }));

        AssertPrintFailed(test.TakeSent());
    }

    [Test]
    public void Favorite_ThenUnfavorite_AnswersOnceEach() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, Single(5, HeldSticker));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        ByteReader reader = Reply(sent[0], 5);
        Assert.That(reader.ReadInt(), Is.EqualTo(HeldSticker));

        // A second mark of the same sticker changes nothing and gets no answer.
        handler.Handle(session, Single(5, HeldSticker));
        Assert.That(test.TakeSent(), Is.Empty);

        handler.Handle(session, Single(6, HeldSticker));
        sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        reader = Reply(sent[0], 6);
        Assert.That(reader.ReadInt(), Is.EqualTo(HeldSticker));

        handler.Handle(session, Single(6, HeldSticker));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase(ExpiredSticker)]
    [TestCase(OtherSticker)]
    public void Favorite_OfAStickerNotUsable_IsNotMarked(int stickerId) {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, Single(5, stickerId));
        Assert.That(test.TakeSent(), Is.Empty);

        // Not marked: an unmark finds nothing to take.
        handler.Handle(session, Single(6, stickerId));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Expired_TakesOnlyTheGroupsRunOutByTheServersClock() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();
        // A favourite of the expired group, marked while it still ran.
        Assert.That(session.Config.TryFavoriteChatSticker(ExpiredSticker), Is.True);
        Assert.That(session.Config.TryFavoriteChatSticker(HeldSticker), Is.True);

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(4);
            writer.WriteInt(HeldGroup);
            writer.WriteInt(ExpiredGroup);
            writer.WriteInt(OtherGroup);
            writer.WriteInt(ExpiredGroup);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        ByteReader reader = Reply(sent[0], 1);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(ExpiredGroup));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(ExpiredSticker));
            Assert.That(reader.Available, Is.EqualTo(0));
            Assert.That(session.Player.Value.Unlock.StickerSets.Keys, Is.EquivalentTo(new[] { HeldGroup }));
        });
        // The held group's favourite stays.
        Assert.That(session.Config.TryFavoriteChatSticker(HeldSticker), Is.False);
    }

    [Test]
    public void Expired_OfGroupsStillRunning_SendsNothing() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(2);
            writer.WriteInt(HeldGroup);
            writer.WriteInt(OtherGroup);
        }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.Player.Value.Unlock.StickerSets.Keys, Is.EquivalentTo(new[] { HeldGroup, ExpiredGroup }));
        });
    }

    [TestCase(-1)]
    [TestCase(1001)]
    public void Expired_WithACountOutOfRange_IsDropped(int count) {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(count);
            writer.WriteInt(ExpiredGroup);
        }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.Player.Value.Unlock.StickerSets.ContainsKey(ExpiredGroup), Is.True);
        });
    }

    // ChatStamp 0 at the field entry: u16 count and the favourite sticker ids, then u16 count and per held group its
    // id and its end time (CChatEmoticonCli::OnEmoticonList, chatemoticoncli.cpp:153-165).
    [Test]
    public void Load_ListsTheFavouritesAndTheHeldGroups() {
        (TestGameSession test, GameSession session, ChatStickerHandler handler) = Setup();
        handler.Handle(session, Single(5, HeldSticker));
        test.TakeSent();
        typeof(ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
        long expired = session.Player.Value.Unlock.StickerSets[ExpiredGroup];

        session.Config.LoadChatStickers();

        ByteReader reader = Reply(test.TakeSent().Single(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadShort(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(HeldSticker));
            Assert.That(reader.ReadShort(), Is.EqualTo(2));
            var groups = new Dictionary<int, long>();
            for (int i = 0; i < 2; i++) {
                int id = reader.ReadInt();
                groups[id] = reader.ReadLong();
            }
            Assert.That(groups, Is.EqualTo(new Dictionary<int, long> { [HeldGroup] = ChatStickerRules.Permanent, [ExpiredGroup] = expired }));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }
}
