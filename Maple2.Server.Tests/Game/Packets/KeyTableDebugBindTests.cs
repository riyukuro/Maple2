using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// The debug mode bind (action 600001) is in the key table packet for a GM account and never for a plain one.
public class KeyTableDebugBindTests {
    // key 41, type 2, action 600001, slot 0, priority 0 as the client reads a bind (21 bytes)
    private static readonly byte[] DebugBind = [
        0x29, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00,
        0xC1, 0x27, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00,
    ];

    private static readonly KeyBind Jump = new(57, 2, 300001, 0, 0);

    private static byte[] Packet(ICollection<KeyBind> stored, bool gm) {
        return KeyTablePacket.Load(KeyBindRules.ForSend(stored, gm), 0, Array.Empty<HotBar>()).ToArray();
    }

    private static bool Contains(byte[] data, byte[] part) {
        for (int i = 0; i + part.Length <= data.Length; i++) {
            if (data.AsSpan(i, part.Length).SequenceEqual(part)) {
                return true;
            }
        }
        return false;
    }

    [Test]
    public void GmAccount_GetsTheBind() {
        byte[] data = Packet([Jump], gm: true);
        Assert.That(Contains(data, DebugBind), Is.True);
        // opcode, command, default flag, then the count: the stored bind and the debug bind
        Assert.That(BitConverter.ToInt32(data, 4), Is.EqualTo(2));
    }

    [Test]
    public void PlainAccount_NeverGetsTheBind() {
        Assert.That(Contains(Packet([Jump], gm: false), DebugBind), Is.False);
        // also not when the stored table holds it (saved from the client's default file)
        var debug = new KeyBind(KeyBindRules.DebugModeKey, 2, KeyBindRules.DebugModeAction, 0, 0);
        byte[] data = Packet([Jump, debug], gm: false);
        Assert.That(Contains(data, DebugBind), Is.False);
        Assert.That(BitConverter.ToInt32(data, 4), Is.EqualTo(1));
    }

    [Test]
    public void GmAccount_KeyInUse_TakesTheOtherKey() {
        var taken = new KeyBind(KeyBindRules.DebugModeKey, 2, 400001, 0, 0);
        ICollection<KeyBind> sent = KeyBindRules.ForSend([Jump, taken], gm: true);
        Assert.That(sent, Does.Contain(taken));
        Assert.That(sent.Single(bind => bind.OptionGuid == KeyBindRules.DebugModeAction).KeyCode, Is.EqualTo(KeyBindRules.DebugModeKeyAlt));
    }

    [Test]
    public void GmAccount_EmptyKeySlot_IsReplacedNotDoubled() {
        var empty = new KeyBind(KeyBindRules.DebugModeKey, 0, 0, 0, 0);
        ICollection<KeyBind> sent = KeyBindRules.ForSend([Jump, empty], gm: true);
        Assert.That(sent.Count(bind => bind.KeyCode == KeyBindRules.DebugModeKey), Is.EqualTo(1));
        Assert.That(sent.Count, Is.EqualTo(2));
    }

    [Test]
    public void EmptyTable_IsLeftForTheClientDefault() {
        Assert.That(KeyBindRules.ForSend([], gm: true), Is.Empty);
        Assert.That(KeyBindRules.ForSend([], gm: false), Is.Empty);
    }
}
