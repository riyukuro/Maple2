using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
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
    private const int Furniture = 50400013;
    private TestGameSession test = null!;

    // Ugc 1 or 3: the mode, the upload head (kind 1, an item, this character, the upload's uid), then for Ugc 1 the
    // item's part.
    private ByteReader Packet(GameSession session, byte mode, long ugcUid, UgcType type = UgcType.Item, int itemId = Design) => TestGameSession.Packet(writer => {
        writer.WriteByte(mode);
        if (mode == 1) {
            writer.WriteLong(0);
        }
        writer.WriteByte((byte) type);
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
            writer.WriteInt(itemId);
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

    // The design's row cannot be written at the confirmation: the fee taken at the upload goes back, once (a later
    // Ugc 0 gives nothing more), and no row is left. Before, the fee was lost.
    [Test]
    public void FailedItemWrite_GivesTheFeeBack() {
        test = new TestGameSession();
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meso, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CL));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meso = 5000;
        var handler = new UgcHandler {
            WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options,
                NullLogger<WebStorage>.Instance),
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            World = null!,
        };
        handler.Handle(session, Packet(session, 1, 0));
        StoredItem staged = session.StagedUgcItem!;
        long paid = session.Currency.Meso;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item");
        try {
            handler.Handle(session, Packet(session, 3, staged.Template!.Id));
        } finally {
            test.FailSave = null;
        }
        long afterConfirm = session.Currency.Meso;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        using var context = new Ms2Context(test.Options);
        Assert.Multiple(() => {
            Assert.That(paid, Is.EqualTo(5000 - 390));
            Assert.That(afterConfirm, Is.EqualTo(5000));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(context.Item.Count(row => row.ItemId == Design), Is.Zero);
            Assert.That(session.Item.Inventory.Find(Design), Is.Empty);
        });
    }

    private UgcHandler Handler(GameSession session) => new() {
        WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options,
            NullLogger<WebStorage>.Instance),
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        World = null!,
    };

    // The paid upload's confirmation finds no staged design of it (here the staged item went, as when a later
    // upload replaced it): the fee goes back, once. Before, Finish took the fee and the confirmation returned.
    [Test]
    public void NoStagedDesign_GivesTheFeeBack() {
        test = new TestGameSession();
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meso, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CL));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meso = 5000;
        UgcHandler handler = Handler(session);
        handler.Handle(session, Packet(session, 1, 0));
        long ugcUid = session.StagedUgcItem!.Template!.Id;
        long paid = session.Currency.Meso;
        session.StagedUgcItem = null;
        handler.Handle(session, Packet(session, 3, ugcUid));
        long afterConfirm = session.Currency.Meso;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        Assert.Multiple(() => {
            Assert.That(paid, Is.EqualTo(5000 - 390));
            Assert.That(afterConfirm, Is.EqualTo(5000));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }

    // A furniture design that the furnishing storage does not take (its write fails): the fee goes back and no row
    // is left.
    [Test]
    public void FurnishingNotTaken_GivesTheFeeBack() {
        test = new TestGameSession();
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Furniture] = new(1, MeretMarketCurrencyType.Meso, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Furniture, slotMax: 100) with {
            Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
            Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
        });
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meso = 5000;
        UgcHandler handler = Handler(session);
        handler.Handle(session, Packet(session, 1, 0, UgcType.Furniture, Furniture));
        long ugcUid = session.StagedUgcItem!.Template!.Id;
        long paid = session.Currency.Meso;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item");
        try {
            handler.Handle(session, Packet(session, 3, ugcUid, UgcType.Furniture));
        } finally {
            test.FailSave = null;
        }
        long afterConfirm = session.Currency.Meso;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        using var context = new Ms2Context(test.Options);
        Assert.Multiple(() => {
            Assert.That(paid, Is.EqualTo(5000 - 390));
            Assert.That(afterConfirm, Is.EqualTo(5000));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(context.Item.Count(row => row.ItemId == Furniture), Is.Zero);
            Assert.That(session.Item.Furnishing.GetItem(Furniture), Is.Null);
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
