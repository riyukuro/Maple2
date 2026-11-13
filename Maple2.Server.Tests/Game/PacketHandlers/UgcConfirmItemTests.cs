using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game.Manager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The design upload's confirmation (Ugc 3): the paid design becomes an item of the character.
public class UgcConfirmItemTests {
    private const int Design = 11300902;
    private const int Town = 90000100;
    private TestGameSession test = null!;

    // Ugc 1 or 3: the mode, the upload head (kind 1, an item, this character, the upload's uid), then for Ugc 1 the
    // item's part.
    private ByteReader Packet(GameSession session, byte mode, long ugcUid) => TestGameSession.Packet(writer => {
        writer.WriteByte(mode);
        if (mode == 1) {
            writer.WriteLong(0);
        }
        writer.WriteByte(1);
        writer.WriteShort(0);
        writer.WriteInt(0);
        writer.WriteLong(session.AccountId);
        writer.WriteLong(session.CharacterId);
        writer.WriteUnicodeString("");
        writer.WriteUnicodeString("");
        writer.WriteLong(ugcUid);
        writer.WriteUnicodeString("");
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteUnicodeString("");
        if (mode == 1) {
            writer.WriteLong(0);
            writer.WriteInt(Design);
            writer.WriteInt(1);
            writer.WriteUnicodeString("shirt");
            writer.WriteByte(0);
            writer.WriteLong(390);
            writer.WriteBool(false);
        }
    });

    // A full bag and a failed mail write: the confirmed design's row stays the character's (the next login places
    // or mails it) and the failure is logged. Before, the result was not read and nothing was logged.
    [Test]
    public void FullBagAndFailedMail_KeepsTheRowAndLogs() {
        test = new TestGameSession();
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meso, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CL));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meso = 5000;
        var errors = new List<string>();
        ILogger before = Log.Logger;
        try {
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(new ErrorLines(errors)).CreateLogger();
            var handler = new UgcHandler {
                WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options,
                    NullLogger<WebStorage>.Instance),
                ItemMetadata = session.ItemMetadata,
                TableMetadata = test.TableMetadata,
                World = null!,
            };
            handler.Handle(session, Packet(session, 1, 0));
            StoredItem staged = session.StagedUgcItem!;
            BagSplitTests.Bag(session, staged, (short) (session.Item.Inventory.Filter(_ => true, staged.Inventory).Count + 1), locked: 1);
            test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                                   && entry.Entity.GetType().Name == "Item");
            handler.Handle(session, Packet(session, 3, staged.Template!.Id));
        } finally {
            Log.Logger = before;
            test.FailSave = null;
        }

        using var context = new Ms2Context(test.Options);
        List<long> owners = context.Item.Where(row => row.ItemId == Design && !row.IsLocked).Select(row => row.OwnerId).ToList();
        Assert.Multiple(() => {
            Assert.That(owners, Is.EqualTo(new[] { test.CharacterId }));
            Assert.That(errors.Count(line => line.Contains("neither added nor mailed")), Is.EqualTo(1));
        });
    }

    private sealed class ErrorLines(List<string> errors) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) {
            if (logEvent.Level >= Serilog.Events.LogEventLevel.Error) {
                errors.Add(logEvent.RenderMessage());
            }
        }
    }
}
