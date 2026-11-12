using System;
using System.Collections.Generic;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.Packets;
using Maple2.Server.World.Containers;
using Maple2.Model.Error;

namespace Maple2.Server.Tests.Game.Pvp;

// The observer pieces: the observer dialogs' layouts (CTriggerService::FUN_141862110 kinds 2 and 5, read by
// FUN_140743ca0 / FUN_140743f50), the rank duel's observe offer (Pvp 0x19) and onlooker flag (0x13), and the World's
// observe join.
public class ObserverTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));

    // After the opcode, the dialog command (15) and the kind, the tick (4 bytes, skipped here), the duration and the
    // auto-remove flag; then per fighter the character, health, total and wins (and, for a duel, the rating), then a
    // duel's center camera.
    [Test]
    public void DialogLayouts() {
        string normal = Hex(TriggerPacket.PvpObserverDialog(30000, new TriggerPacket.PvpFighter(11, 900, 1000, 2), new TriggerPacket.PvpFighter(22, 50, 800, 0)));
        Assert.That(normal[..8], Is.EqualTo("4E00" + "0F" + "02"));
        Assert.That(normal[16..], Is.EqualTo("30750000" + "01"
            + Long(11) + "84030000" + "E8030000" + "02"
            + Long(22) + "32000000" + "20030000" + "00"));
        string duel = Hex(TriggerPacket.PvpPlayerDialog(30000, new TriggerPacket.PvpFighter(11, 900, 1000, 0, 1030), new TriggerPacket.PvpFighter(22, 50, 800, 0, 990)));
        Assert.That(duel[..8], Is.EqualTo("4E00" + "0F" + "05"));
        Assert.That(duel[16..], Is.EqualTo("30750000" + "01"
            + Long(11) + "84030000" + "E8030000" + "00" + "06040000"
            + Long(22) + "32000000" + "20030000" + "00" + "DE030000"
            + "00000000"));
        // A character id of 0 closes the dialog.
        Assert.That(Hex(TriggerPacket.PvpObserverDialog(0, default, default))[16..], Is.EqualTo("00000000" + "01" + new string('0', 34 * 2)));
    }

    [Test]
    public void OfferAndOnlooker() {
        Assert.That(Hex(PvpPacket.RankDuelObserveOffer()), Is.EqualTo("B600" + "19"));
        Assert.That(Hex(PvpPacket.RankDuelObserving(false)), Is.EqualTo("B600" + "13" + "00"));
        Assert.That(Hex(PvpPacket.RankDuelObserving(true)), Is.EqualTo("B600" + "13" + "01"));
    }

    // A match can be watched once its fight started; a character with no match of its own watches the latest.
    [Test]
    public void Queue_Observe() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        queue.Register(2, 0, out RankDuelMatch? first);
        queue.SetRoom(first!.Id, 1, 65010001, 10);
        queue.Register(3, 0, out _);
        queue.Register(4, 0, out RankDuelMatch? second);
        queue.SetRoom(second!.Id, 1, 65010002, 11);
        Assert.That(queue.Observe(9), Is.Null);
        queue.SetObservable(first.Id, 100);
        queue.SetObservable(second.Id, 200);
        Assert.That(queue.Observe(9), Is.SameAs(second));
        Assert.That(queue.Observe(1), Is.Null);
        queue.End(second.Id);
        Assert.That(queue.Observe(9), Is.SameAs(first));
    }

    // The World: the fight start offers the duel (the broadcast runs once); a join with no match of its own is an
    // observer's join into that field.
    [Test]
    public void Lookup_ObserveJoin() {
        var made = new List<int>();
        int offers = 0;
        var lookup = new PvpLookup((matchId, arena, ids) => {
            made.Add(matchId);
            return (MigrationError.ok, 1, 65010003, 700 + matchId);
        }, (_, _, _) => { }, () => 0);
        lookup.SetOffer(() => offers++);
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        Assert.That(lookup.Join(9, out _, out _, out _, out bool observer), Is.EqualTo(RankDuelError.NoMatch));
        lookup.Observable(made[0]);
        Assert.That(offers, Is.EqualTo(1));
        Assert.That(lookup.Join(9, out int channel, out int mapId, out int roomId, out observer), Is.EqualTo(RankDuelError.none));
        Assert.That((channel, mapId, roomId, observer), Is.EqualTo((1, 65010003, 700 + made[0], true)));
        // A duelist's own join is not an observer's.
        Assert.That(lookup.Join(1, out _, out _, out _, out observer), Is.EqualTo(RankDuelError.none));
        Assert.That(observer, Is.False);
    }
}
