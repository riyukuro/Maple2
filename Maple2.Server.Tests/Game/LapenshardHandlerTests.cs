using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// The Lapenshard packet (RecvOp.Lapenshard): the fusion dialog's requests. A request the client's own checks would
// not let through sends nothing back and takes nothing.
public class LapenshardHandlerTests {
    private const int Shard = 20000001;
    private TestGameSession test = null!;
    private LapenshardHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new LapenshardUpgradeTable(new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [Shard] = new(1, 1, Shard + 1, 2, [], 500),
        }));
        test.Start();
        handler = new LapenshardHandler { TableMetadata = test.TableMetadata, ItemMetadata = test.ItemMetadata };
        test.Session.Currency.ChangeMeso(10_000);
        test.TakeSent();
    }

    private void Send(byte command, System.Action<Maple2.PacketLib.Tools.ByteWriter> body) {
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body(writer);
        }));
    }

    [Test]
    public void AddLapenshard_NotHeldSendsNoPreview() {
        Send(3, writer => {
            writer.WriteLong(999);
            writer.WriteInt(Shard);
            writer.WriteInt(0);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AddFodder_NotHeldSendsNoPreview() {
        Send(4, writer => {
            writer.WriteLong(999);
            writer.WriteInt(Shard);
            writer.WriteInt(0);
            writer.WriteInt(1);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Upgrade_UnknownShardTakesNothing() {
        Send(5, writer => {
            writer.WriteLong(0);
            writer.WriteInt(1234);
            writer.WriteInt(0);
            writer.WriteInt(0);
        });

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Session.Currency.Meso, Is.EqualTo(10_000));
    }

    [Test]
    public void Upgrade_WrongFodderCountTakesNothing() {
        Send(5, writer => {
            writer.WriteLong(0);
            writer.WriteInt(Shard);
            writer.WriteInt(0);
            writer.WriteInt(1);
            writer.WriteLong(55);
            writer.WriteInt(1);
        });

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Session.Currency.Meso, Is.EqualTo(10_000));
    }

    [Test]
    public void Upgrade_ShardNotEquippedTakesNothing() {
        Send(5, writer => {
            writer.WriteLong(0);
            writer.WriteInt(Shard);
            writer.WriteInt(0);
            writer.WriteInt(1);
            writer.WriteLong(55);
            writer.WriteInt(2);
        });

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Session.Currency.Meso, Is.EqualTo(10_000));
    }
}
