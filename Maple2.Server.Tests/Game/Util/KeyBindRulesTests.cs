using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Client senders: client/ms2/keytable.cpp:385 and :407 (count, then key code, type, code, slot, priority each);
// the key codes are the ones the client's key table is filled with (client/ms2/keytable.cpp:622).
public class KeyBindRulesTests {
    [Test]
    public void ClientBytes_UnknownKeyCodesAreDropped() {
        int[] codes = [1, 0xCF, 0xD0, 0xF00501, 0xF10812, 0x7FFFFFFF, -1, 0];
        var writer = new ByteWriter();
        writer.WriteInt(codes.Length);
        foreach (int code in codes) {
            writer.WriteInt(code);
            writer.WriteInt(1);
            writer.WriteLong(10);
            writer.WriteInt(0);
            writer.WriteByte(2);
        }
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);

        int count = reader.ReadInt();
        var kept = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++) {
            var keyBind = reader.Read<KeyBind>();
            Assert.That(keyBind.OptionGuid, Is.EqualTo(10));
            Assert.That(keyBind.Priority, Is.EqualTo(2));
            if (KeyBindRules.IsClientKeyCode(keyBind.KeyCode)) {
                kept.Add(keyBind.KeyCode);
            }
        }

        Assert.That(reader.Position, Is.EqualTo(4 + codes.Length * 21));
        Assert.That(kept, Is.EqualTo(new[] { 1, 0xCF, 0xF00501, 0xF10812 }));
    }

    [Test]
    public void IsClientKeyCode_CountsTheClientTable() {
        int count = 0;
        for (int code = 0; code <= 0xFFFFFF; code++) {
            if (KeyBindRules.IsClientKeyCode(code)) {
                count++;
            }
        }
        // 207 single keys and 4 * (12 + 16) combined keys.
        Assert.That(count, Is.EqualTo(207 + 112));
    }

    [TestCase(0xF00505)] // second key is one of the first keys
    [TestCase(0xF00411)]
    [TestCase(0xF10511)]
    [TestCase(0xF20501)]
    [TestCase(0x1F00501)]
    public void IsClientKeyCode_RefusesOtherCombinations(int code) {
        Assert.That(KeyBindRules.IsClientKeyCode(code), Is.False);
    }

    // The debug mode bind a GM account's table carries sits on key 41 or key 70; both are client key codes,
    // so a GM client that sends its table back keeps the bind.
    [Test]
    public void GmTableKeepsDebugKeys() {
        Assert.That(KeyBindRules.IsClientKeyCode(KeyBindRules.DebugModeKey), Is.True);
        Assert.That(KeyBindRules.IsClientKeyCode(KeyBindRules.DebugModeKeyAlt), Is.True);

        var stored = new List<KeyBind> { new KeyBind(17, 1, 100, 0, 0) };
        ICollection<KeyBind> gmTable = KeyBindRules.ForSend(stored, gm: true);
        Assert.That(gmTable.Any(bind => bind.OptionGuid == KeyBindRules.DebugModeAction), Is.True);
        Assert.That(gmTable.All(bind => KeyBindRules.IsClientKeyCode(bind.KeyCode)), Is.True);

        // Key 41 taken by another action: the bind moves to key 70, still a client key code.
        stored = new List<KeyBind> { new KeyBind(KeyBindRules.DebugModeKey, 1, 100, 0, 0) };
        gmTable = KeyBindRules.ForSend(stored, gm: true);
        Assert.That(gmTable.Single(bind => bind.OptionGuid == KeyBindRules.DebugModeAction).KeyCode, Is.EqualTo(KeyBindRules.DebugModeKeyAlt));
        Assert.That(gmTable.All(bind => KeyBindRules.IsClientKeyCode(bind.KeyCode)), Is.True);
    }
}
