using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// ChatSticker sub-type 1: the client lists the groups it thinks have run out; the answer (chat stamp mode 1,
// chatemoticoncli.cpp FUN_140a39170) is int count + group ids, then int count + favourite sticker ids.
public class ChatStickerExpiredTests {
    [Test]
    public void Reply_IsMode1_GroupsThenFavorites() {
        ByteWriter writer = ChatStickerPacket.ExpiredNotify(new[] { 7, 0x01020304 }, new[] { 90 });
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

        Assert.That(body, Is.EqualTo(new byte[] {
            0x01, // mode
            0x02, 0x00, 0x00, 0x00, // groups
            0x07, 0x00, 0x00, 0x00,
            0x04, 0x03, 0x02, 0x01,
            0x01, 0x00, 0x00, 0x00, // favourites
            0x5A, 0x00, 0x00, 0x00,
        }));
    }

    [Test]
    public void OnlyHeldGroupsPastTheirTime_AreDropped() {
        var held = new Dictionary<int, long> { [1] = 100, [2] = 500, [3] = long.MaxValue, [4] = 99 };

        Assert.That(ChatStickerRules.Expired(held, new[] { 1, 2, 3, 9, 1 }, 200), Is.EqualTo(new[] { 1 }));
        Assert.That(ChatStickerRules.Expired(held, Array.Empty<int>(), 200), Is.Empty);
        Assert.That(ChatStickerRules.Expired(held, new[] { 4, 1 }, 100), Is.EqualTo(new[] { 4 }));
    }
}
