using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// What a full bag does to an item the player is owed or already paid for: it comes by mail, or nothing is taken.
public class BagFullRewardTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long GuildId = 7;
    private TestGameSession test = null!;

    private GameSession Start(params ItemMetadata[] metadata) {
        test = new TestGameSession();
        test.Register(metadata);
        GameSession session = test.Start();
        // Items are owned by a character, account or mail id; the in-memory database starts each at 1, so one mail
        // id is used up first (see BagSplitTests.Mail).
        using GameStorage.Request db = test.Storage.Context();
        db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        return session;
    }

    // The bag tab of the item with no open slot: its last slot holds a locked stack, which takes no add.
    private static void FullBag(GameSession session, StoredItem sample) =>
        BagSplitTests.Bag(session, sample, (short) (session.Item.Inventory.Filter(_ => true, sample.Inventory).Count + 1), locked: 1);

    private int Mailed(int itemId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(test.CharacterId).SelectMany(mail => mail.Items).Where(item => item.Id == itemId).Sum(item => item.Amount);
    }

    private static GuildTable.Property Property(int checkInCoin = 0, int winCoin = 0) {
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        return property with { Level = 1, Capacity = 10, CheckInCoin = checkInCoin, WinMiniGameCoin = winCoin };
    }

    private void Join(GameSession session, GuildTable.Property property) {
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property }));
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        typeof(GuildManager).GetProperty("Properties", Any)!.SetValue(manager, property);
        var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Self", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        var self = new GuildMember { GuildId = GuildId, Info = info, Rank = 0, JoinTime = 1000 };
        var guild = new Guild(GuildId, "Guild", self);
        guild.Members[self.CharacterId] = self;
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    // The check-in's guild coin with a full bag: the World has taken the check-in, so the coin comes by mail
    // (before, the bag's refusal dropped it).
    [Test]
    public void GuildCheckIn_FullBag_MailsTheCoin() {
        GameSession session = Start(TestGameSession.Metadata(Constant.GuildCoinId));
        Join(session, Property(checkInCoin: 5));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        FullBag(session, session.Field!.ItemDrop.CreateItem(Constant.GuildCoinId)!);
        var handler = new GuildHandler {
            World = test.World,
            TableMetadata = test.TableMetadata,
            BanWordStorage = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        };
        test.World.GuildWorld = _ => new GuildResponse { GuildId = GuildId };

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(15)));

        Assert.Multiple(() => {
            Assert.That(test.World.GuildRequests.Single().CheckIn.GuildId, Is.EqualTo(GuildId));
            Assert.That(BagSplitTests.Held(session, Constant.GuildCoinId), Is.Zero);
            Assert.That(Mailed(Constant.GuildCoinId), Is.EqualTo(5));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The guild vs game's coin reward with a full bag comes by mail.
    [Test]
    public void GuildVsGameCoin_FullBag_MailsTheCoin() {
        GameSession session = Start(TestGameSession.Metadata(Constant.GuildCoinId));
        Join(session, Property(winCoin: 7));
        FullBag(session, session.Field!.ItemDrop.CreateItem(Constant.GuildCoinId)!);
        var game = new GuildVsGameField(session.Field);
        game.SetMatch(1, GuildId, GuildId + 1, _ => { });
        game.Arrive(session.Player);

        game.GiveReward("guildCoin", GuildVsGameState.BlueTeam, isWin: true);

        Assert.Multiple(() => {
            Assert.That(BagSplitTests.Held(session, Constant.GuildCoinId), Is.Zero);
            Assert.That(Mailed(Constant.GuildCoinId), Is.EqualTo(7));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A mail of an item row whose move fails leaves no empty mail, and the row keeps its owner and place. Before,
    // the empty mail stayed in the inbox.
    [Test]
    public void MailHeldItem_FailedMove_LeavesNoMail() {
        GameSession session = Start(TestGameSession.Metadata(Constant.GuildCoinId));
        StoredItem row;
        using (GameStorage.Request db = test.Storage.Context()) {
            row = db.CreateItem(test.CharacterId, session.Field!.ItemDrop.CreateItem(Constant.GuildCoinId)!)!;
        }
        short slot = row.Slot;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                               && entry.Entity.GetType().Name == "Item");

        bool mailed = session.Item.MailHeldItem(row);
        test.FailSave = null;

        using GameStorage.Request reload = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(mailed, Is.False);
            Assert.That(reload.GetAllMail(test.CharacterId), Is.Empty);
            Assert.That(row.Slot, Is.EqualTo(slot));
            Assert.That(reload.GetItemGroups(test.CharacterId, ItemGroup.Default)[ItemGroup.Default].Single().Uid, Is.EqualTo(row.Uid));
        });
    }

    // A mail of a new item whose row write fails leaves neither the mail nor an item row. Before, the mail's delete
    // ran on the failed request and wrote the item row again, owned by the deleted mail.
    [Test]
    public void MailItem_FailedCreate_LeavesNoRow() {
        GameSession session = Start(TestGameSession.Metadata(Constant.GuildCoinId));
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item");

        bool mailed = session.Item.MailItem(session.Field!.ItemDrop.CreateItem(Constant.GuildCoinId)!);
        test.FailSave = null;

        using var context = new Ms2Context(test.Options);
        using GameStorage.Request reload = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(mailed, Is.False);
            Assert.That(reload.GetAllMail(test.CharacterId), Is.Empty);
            Assert.That(context.Item.Count(item => item.ItemId == Constant.GuildCoinId), Is.Zero);
        });
    }

    private GameSession StartBlueprint() {
        GameSession session = Start(TestGameSession.Metadata(Constant.BlueprintId, type: 22, slotMax: 1));
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Constant.BlueprintId] = new(1, MeretMarketCurrencyType.Meret, 300, 0, 0),
        }));
        test.EnterHome();
        session.Currency.Meret = 1000;
        test.TakeSent();
        return session;
    }

    // An add that fails after the room check (its bag save fails): the blueprint goes by mail and request 63 is
    // answered with ok 0, which closes the client's upload dialog. Before, nothing answered it.
    [Test]
    public void CreateBlueprint_FailedBagSave_MailsAndAnswers() {
        GameSession session = StartBlueprint();
        int fails = 1;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                               && entry.Entity.GetType().Name == "Item") && fails-- > 0;

        CreateBlueprint(session);
        test.FailSave = null;

        List<byte[]> answers = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponseCube).ToList();
        Assert.Multiple(() => {
            Assert.That(answers.Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(CubePacket.CreateBlueprintFailed().ToArray()) }));
            Assert.That(answers.Single()[2..], Is.EqualTo(new byte[] { 63, 0 }));
            Assert.That(Mailed(Constant.BlueprintId), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Find(Constant.BlueprintId), Is.Empty);
        });
    }

    private void CreateBlueprint(GameSession session) =>
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(63)));

    private int LayoutRows() {
        using var context = new Ms2Context(test.Options);
        return context.HomeLayout.Count();
    }

    // A blueprint with a full bag: refused with the bag's notice before anything is paid or written. Before, the
    // merets were taken first and the blueprint and its layout row were lost.
    [Test]
    public void CreateBlueprint_FullBag_TakesNothing() {
        GameSession session = StartBlueprint();
        FullBag(session, session.Field!.ItemDrop.CreateItem(Constant.BlueprintId)!);
        test.TakeSent();

        CreateBlueprint(session);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(LayoutRows(), Is.Zero);
            Assert.That(session.Item.Inventory.Find(Constant.BlueprintId).Count(item => !item.IsLocked), Is.Zero);
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemInventory }));
        });
    }

    // With room: the answer carries the uid of the item in the bag, the item the upload then works on. Before, it
    // carried 0 (the add put a copy with the new row's uid in the bag), so the client could not find the item.
    [Test]
    public void CreateBlueprint_AnswersWithTheBagItemsUid() {
        GameSession session = StartBlueprint();

        CreateBlueprint(session);

        StoredItem held = session.Item.Inventory.Find(Constant.BlueprintId).Single();
        byte[] answer = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.ResponseCube);
        var reader = new ByteReader(answer, 2);
        Assert.Multiple(() => {
            Assert.That(held.Uid, Is.Not.Zero);
            Assert.That(reader.ReadByte(), Is.EqualTo(63));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(held.Uid));
            Assert.That(session.StagedUgcItem, Is.SameAs(held));
            Assert.That(session.Currency.Meret, Is.EqualTo(700));
            Assert.That(LayoutRows(), Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A start item with a full bag comes by mail, once: the mailed pieces count as given on the next request
    // (before, the bag's refusal dropped it).
    [Test]
    public void TutorialItem_FullBag_MailsTheItemOnce() {
        const int itemId = 30000001;
        GameSession session = Start(TestGameSession.Metadata(itemId), TestGameSession.Metadata(itemId + 1));
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(session.Field!.MapId, 0, 0, [], [], [new JobTable.Item(itemId, 1, 3)], []),
                new Dictionary<SkillRank, JobTable.Skill[]>(), []),
        }));
        FullBag(session, session.Field.ItemDrop.CreateItem(itemId + 1)!);
        var handler = new TutorialItemHandler();

        handler.Handle(session, TestGameSession.Packet(_ => { }));
        handler.Handle(session, TestGameSession.Packet(_ => { }));

        Assert.Multiple(() => {
            Assert.That(BagSplitTests.Held(session, itemId), Is.Zero);
            Assert.That(Mailed(itemId), Is.EqualTo(3));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
