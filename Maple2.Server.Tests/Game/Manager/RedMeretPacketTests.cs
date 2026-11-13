using System;
using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The client keeps the red merets (GameMeret) apart from the merets: the Meret packet's four longs are added up as
// the merets (CSceneStateGame::OnMeret), the red balance comes from the field enter's fifth wallet long and from
// MeretUse (OnServerEnter, OnMeretUse). Before, the red merets were sent as the Meret packet's third long, so the
// client added them to the merets and its red balance stayed 0.
public class RedMeretPacketTests {
    private static long Long(byte[] packet, int index) => BitConverter.ToInt64(packet, 2 + 8 * index);
    private static ushort Op(byte[] packet) => BitConverter.ToUInt16(packet, 0);

    private static Currency Wallet() => new() { Meret = 100, GameMeret = 50 };

    [Test]
    public void Meret_PlainMeretsOnly() {
        byte[] packet = CurrencyPacket.UpdateMeret(Wallet(), -7).ToArray();

        Assert.Multiple(() => {
            Assert.That(Op(packet), Is.EqualTo((ushort) SendOp.Meret));
            Assert.That(packet, Has.Length.EqualTo(2 + 5 * 8));
            Assert.That(new[] { Long(packet, 0), Long(packet, 1), Long(packet, 2), Long(packet, 3) }, Is.EqualTo(new[] { 100L, 0, 0, 0 }));
            Assert.That(Long(packet, 4), Is.EqualTo(-7));
        });
    }

    [Test]
    public void MeretUse_RedBalanceAndChange() {
        byte[] packet = CurrencyPacket.UpdateGameMeret(Wallet(), 20).ToArray();

        Assert.Multiple(() => {
            Assert.That(Op(packet), Is.EqualTo((ushort) 0x117));
            Assert.That(packet, Has.Length.EqualTo(2 + 2 * 8));
            Assert.That(Long(packet, 0), Is.EqualTo(50));
            Assert.That(Long(packet, 1), Is.EqualTo(20));
        });
    }

    // OnServerEnter reads int object id, long character id, short channel, long exp, long rest exp, long meso, then
    // the wallet: four longs added up as the merets and the red merets.
    [Test]
    public void ServerEnter_RedMeretsAreTheFifthWalletLong() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Currency.Meret = 100;
        session.Player.Value.Currency.GameMeret = 50;

        byte[] packet = ServerEnterPacket.Request(session.Player).ToArray();
        const int wallet = 2 + 4 + 8 + 2 + 8 + 8 + 8;
        long[] longs = new long[5];
        for (int i = 0; i < longs.Length; i++) {
            longs[i] = BitConverter.ToInt64(packet, wallet + 8 * i);
        }

        Assert.That(longs, Is.EqualTo(new[] { 100L, 0, 0, 0, 50 }));
    }

    // Every red meret change sends MeretUse with the red balance; none sends the Meret packet.
    [Test]
    public void GameMeretChanges_SendMeretUse() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meret = 100;
        test.TakeSent();

        session.Currency.GameMeret = 60;
        List<byte[]> set = test.TakeSent();
        session.Currency.ChangeGameMeret(-10);
        List<byte[]> changed = test.TakeSent();
        Assert.That(session.Currency.TryTakeGameMeret(5), Is.True);
        List<byte[]> taken = test.TakeSent();
        Assert.That(session.Currency.TryTakeGameMeret(15, notify: false), Is.True);
        List<byte[]> quiet = test.TakeSent();
        session.Currency.NotifyGameMeretTaken(15);
        List<byte[]> notified = test.TakeSent();
        session.Currency.ReturnGameMeret(15);
        List<byte[]> back = test.TakeSent();

        Assert.Multiple(() => {
            foreach (List<byte[]> sent in new[] { set, changed, taken, notified, back }) {
                Assert.That(sent.ConvertAll(Op), Is.EqualTo(new[] { (ushort) SendOp.MeretUse }));
            }
            Assert.That(quiet, Is.Empty);
            Assert.That((Long(set[0], 0), Long(set[0], 1)), Is.EqualTo((60L, 60L)));
            Assert.That((Long(changed[0], 0), Long(changed[0], 1)), Is.EqualTo((50L, -10L)));
            Assert.That((Long(taken[0], 0), Long(taken[0], 1)), Is.EqualTo((45L, -5L)));
            Assert.That((Long(notified[0], 0), Long(notified[0], 1)), Is.EqualTo((30L, -15L)));
            Assert.That((Long(back[0], 0), Long(back[0], 1)), Is.EqualTo((45L, 0L)));
            Assert.That(session.Currency.Meret, Is.EqualTo(100));
        });
    }

    // With red merets held, the Meret packet's sum (what the client shows as the merets) is the plain merets only.
    [Test]
    public void MeretSum_LeavesOutRedMerets() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.GameMeret = 50;
        test.TakeSent();

        session.Currency.ChangeMeret(30);
        List<byte[]> sent = test.TakeSent();

        Assert.That(sent.ConvertAll(Op), Is.EqualTo(new[] { (ushort) SendOp.Meret }));
        long sum = Long(sent[0], 0) + Long(sent[0], 1) + Long(sent[0], 2) + Long(sent[0], 3);
        Assert.Multiple(() => {
            Assert.That(sum, Is.EqualTo(30));
            Assert.That(Long(sent[0], 4), Is.EqualTo(30));
        });
    }
}
