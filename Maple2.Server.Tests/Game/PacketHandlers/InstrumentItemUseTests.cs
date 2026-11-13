using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Using an instrument item (OpenInstrument): the client enters its play state itself and then sends RequestItemUse
// (the item uid and an empty argument) with no reply read. The server answers nothing and changes nothing; the
// play itself starts with PlayInstrument 0.
public class InstrumentItemUseTests {
    private const int InstrumentItemId = 34000001;

    [Test]
    public void OpenInstrument_ItemUse_IsTakenSilently_AndThePlayStillStarts() {
        var test = new TestGameSession();
        ItemMetadata instrument = TestGameSession.Metadata(InstrumentItemId,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", "6", false), type: 6);
        test.Register(instrument);
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
            [6] = new(6, 0, 3, 1, 0, 0),
        }));
        Item item = test.Store(instrument);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();

        new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteLong(item.Uid);
                writer.WriteUnicodeString("");
            }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
            Assert.That(session.Instrument, Is.Null);
            Assert.That(test.Errors, Is.Empty);
        });

        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(0);
                writer.WriteLong(item.Uid);
            }));
        Assert.That(session.Instrument?.Improvising, Is.True);
    }
}
