using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class CharacterListSkinTabTests {
    private static byte[] Entry(long start, long end) {
        var account = new Account { Username = "user" };
        var character = new Character {
            Name = "name",
            Mastery = new Mastery(),
            SkinTabStart = start,
            SkinTabEnd = end,
        };
        character.ReturnMaps.Push(2000062);
        var entries = new List<(Character, IDictionary<ItemGroup, List<Item>>)> {
            (character, new Dictionary<ItemGroup, List<Item>>()),
        };
        ByteWriter writer = CharacterListPacket.AddEntries(account, entries);
        return writer.Buffer.AsSpan(0, writer.Length).ToArray();
    }

    // The client reads a bool after the badges and, only when it is set, the tab period and item count.
    [Test]
    public void Entry_WithoutSecondTab_EndsWithFalse() {
        byte[] plain = Entry(0, 0);
        Assert.That(plain[^1], Is.EqualTo(0));
    }

    [Test]
    public void Entry_WithSecondTab_WritesPeriodAndItemCount() {
        byte[] plain = Entry(0, 0);
        byte[] tab = Entry(0x1122334455667788, 0x0102030405060708);

        Assert.That(tab.Length, Is.EqualTo(plain.Length + 17));
        Assert.That(tab.AsSpan(0, plain.Length - 1).ToArray(), Is.EqualTo(plain.AsSpan(0, plain.Length - 1).ToArray()));
        Assert.That(tab[plain.Length - 1], Is.EqualTo(1));
        Assert.That(BitConverter.ToInt64(tab, plain.Length), Is.EqualTo(0x1122334455667788));
        Assert.That(BitConverter.ToInt64(tab, plain.Length + 8), Is.EqualTo(0x0102030405060708));
        Assert.That(tab[^1], Is.EqualTo(0));
    }
}
