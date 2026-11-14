using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Item functions through ItemUseHandler.Handle, each from a KR item: the scroll and coupon functions open their window
// (the client opens it from the server's answer, e.g. UIEnchantScrollDialog::FUN_140f3b920) and keep the item, which
// the window's own request uses up; the title scroll and the couple effect box are used up here.
public class ItemFunctionUseTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private Item Login(int id, ItemFunction function, string parameters, int amount = 1, Action? setup = null, ItemTag tag = ItemTag.None,
                       MapMetadata? field = null) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(id, function: new ItemMetadataFunction(function, function.ToString(), parameters, false));
        Item item = test.Store(metadata with { Property = metadata.Property with { Tag = tag } }, amount);
        setup?.Invoke();
        test.Start();
        test.EnterMap(field ?? TestGameSession.Map(2000062));
        test.TakeSent();
        return item;
    }

    private void Use(Item item, string? argument = null) {
        new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteLong(item.Uid);
                if (argument != null) {
                    writer.WriteUnicodeString(argument);
                }
            }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    // 31000001 names the enchant scroll row 20291001 (KR: a +1 scroll for rarity 1, levels 20-29).
    [Test]
    public void EnchantScroll_OpensTheScrollsRow() {
        var row = new EnchantScrollMetadata(EnchantScrollType.Enchant, 20, 29, [1], [30, 31, 32, 33, 34, 50, 51, 52, 53, 54, 55, 56, 14, 15, 22],
            [1], [10000]);
        Item scroll = Login(31000001, ItemFunction.EnchantScroll, "20291001",
            setup: () => test.SetTable(new EnchantScrollTable(new Dictionary<int, EnchantScrollMetadata> { [20291001] = row })));

        Use(scroll);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(EnchantScrollPacket.UseScroll(scroll, row)) }));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(1));
    }

    [Test]
    public void EnchantScroll_WithNoRowIsAnInvalidScroll() {
        Item scroll = Login(31000001, ItemFunction.EnchantScroll, "20291001",
            setup: () => test.SetTable(new EnchantScrollTable(new Dictionary<int, EnchantScrollMetadata>())));

        Use(scroll);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(EnchantScrollPacket.Error(EnchantScrollError.s_enchantscroll_invalid_scroll)) }));
    }

    // 30000914 names the remake scroll row 10000002; the window's own request reads the row.
    [Test]
    public void ItemRemakeScroll_OpensTheWindow() {
        Item scroll = Login(30000914, ItemFunction.ItemRemakeScroll, "10000002");

        Use(scroll);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesScrollPacket.UseScroll(scroll)) }));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(1));
    }

    // 20500001 names beauty shop 601: the answer is ItemUse 6, which the client sends back as Beauty 23 (SendBeauty).
    [Test]
    public void ItemChangeBeauty_AsksTheClientToOpenTheShop() {
        Item coupon = Login(20500001, ItemFunction.ItemChangeBeauty, "601");

        Use(coupon);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ItemUsePacket.BeautyCoupon(session.Player.ObjectId, coupon.Uid)) }));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(1));
    }

    // 20000680 gives title 10000393; a second scroll of a title the character has is kept.
    [Test]
    public void TitleScroll_GivesTheTitleOnce() {
        Item scroll = Login(20000680, ItemFunction.TitleScroll, "10000393", amount: 2);

        Use(scroll);

        Assert.That(session.Player.Value.Unlock.HasTitle(10000393), Is.True);
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.UserEnv));

        Use(scroll);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_title_scroll_duplicate_err)) }));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(1));
    }

    // 39000001 opens story book 39000001 and is kept (it can be read again).
    [Test]
    public void StoryBook_OpensTheBook() {
        Item book = Login(39000001, ItemFunction.StoryBook, "39000001");

        Use(book);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(StoryBookPacket.Load(39000001)) }));
        Assert.That(session.Item.Inventory.Get(book.Uid)?.Amount, Is.EqualTo(1));
    }

    // 31000193 names the socket scroll row 10000001 (KR: one socket of a level 50 rarity 4 type 12 item, 70%).
    [Test]
    public void ItemSocketScroll_OpensTheScrollsRow() {
        var row = new ItemSocketScrollMetadata(50, 50, [12], [4], 1, 0, 7000);
        Item scroll = Login(31000193, ItemFunction.ItemSocketScroll, "10000001",
            setup: () => test.SetTable(new ItemSocketScrollTable(new Dictionary<int, ItemSocketScrollMetadata> { [10000001] = row })));

        Use(scroll);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ItemSocketScrollPacket.UseScroll(scroll, row)) }));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(1));
    }

    // 20700001 gives badge 70800001 at rarity 4 to the character and, by mail, to the named partner, each naming the other.
    [Test]
    public void OpenCoupleEffectBox_GivesABadgeToEachPartner() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var partner = new TestGameSession(options);
        partner.Start();
        test = new TestGameSession(options);
        test.Register(TestGameSession.Metadata(70800001));
        Item box = test.Store(TestGameSession.Metadata(20700001,
            function: new ItemMetadataFunction(ItemFunction.OpenCoupleEffectBox, "OpenCoupleEffectBox", "70800001,4", false)));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        test.World.AddPlayer(partner.AccountId, partner.CharacterId, partner.Session.PlayerName);
        test.TakeSent();

        Use(box, partner.Session.PlayerName);

        Assert.That(session.Item.Inventory.Get(box.Uid), Is.Null);
        Item badge = session.Item.Inventory.Filter(item => item.Id == 70800001).Single();
        Assert.That((badge.Rarity, badge.CoupleInfo?.CharacterId), Is.EqualTo((4, partner.CharacterId)));
        Assert.That(test.World.MailNotifications.Select(mail => mail.CharacterId), Is.EqualTo(new[] { partner.CharacterId }));
        using GameStorage.Request db = test.Storage.Context();
        Mail? mail = db.GetMail(test.World.MailNotifications[0].MailId, partner.CharacterId);
        Assert.That(mail?.Items.Select(item => (item.Id, item.Rarity, item.CoupleInfo?.CharacterId)),
            Is.EqualTo(new[] { (70800001, 4, (long?) test.CharacterId) }));
    }

    // 21100001 adds sticker set 1 for good; a second coupon of a set held for good is kept.
    [Test]
    public void ChatEmoticonAdd_AddsTheSetOnce() {
        Item coupon = Login(21100001, ItemFunction.ChatEmoticonAdd, "<v id=\"1\"/>", amount: 2);

        Use(coupon);

        Assert.That(session.Player.Value.Unlock.StickerSets.TryGetValue(1, out long expiry), Is.True);
        Assert.That(Sent(), Does.Contain(Hex(ChatStickerPacket.Add(coupon, new ChatSticker(1, expiry)))));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(1));

        Use(coupon);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed_already_exist)) }));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(1));
    }

    // 20300031 (tag air_taxi) takes one ticket to a field the character has been to, and none to one it has not
    // (only the advanced ticket goes there). The ride stops at PrepareField: the target is not in the played client.
    [Test]
    public void CallAirTaxi_TakesOneTicketToAVisitedField() {
        const int to = 2000914;
        const string xBlock = "item_air_taxi_test";
        string mesh = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navmeshes", $"{xBlock}.navmesh");
        Directory.CreateDirectory(Path.GetDirectoryName(mesh)!);
        bool made = !System.IO.File.Exists(mesh);
        if (made) {
            System.IO.File.WriteAllBytes(mesh, []);
        }
        try {
            MapMetadata Field(int id, bool notInPlayedClient = false) => TestGameSession.Map(id, notInPlayedClient: notInPlayedClient) with {
                XBlock = xBlock,
                CashCall = new MapMetadataCashCall(true, true, false, false, false),
                Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
            };
            MapMetadata from = Field(2000913);
            Item ticket = Login(20300031, ItemFunction.CallAirTaxi, "", amount: 2, tag: ItemTag.air_taxi, field: from,
                setup: () => test.AddMap(from, Field(to, notInPlayedClient: true)));
            // CheckMapCashCall looks the target up in the field's map storage.
            typeof(FieldManager).GetField("<MapMetadata>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session.Field, test.MapMetadata);

            Use(ticket, $"{to}");

            Assert.That(session.Item.Inventory.Get(ticket.Uid)?.Amount, Is.EqualTo(2));

            session.Player.Value.Unlock.Maps.Add(to);
            Use(ticket, $"{to}");

            Assert.That(session.Item.Inventory.Get(ticket.Uid)?.Amount, Is.EqualTo(1));
        } finally {
            if (made) {
                System.IO.File.Delete(mesh);
            }
        }
    }

    // 20301053 opens a portal to field 81000001 for 180 s, for up to 20 players, and is used up. The test has no such
    // field, so the walk through the portal stops at the field factory with the move error.
    [Test]
    public void OpenMassive_OpensThePortalAndIsUsedUp() {
        Item item = Login(20301053, ItemFunction.OpenMassive, "<v fieldID=\"81000001\" portalDurationTick=\"180000\" maxCount=\"20\" />");
        typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Field, new ConcurrentDictionary<int, FieldPortal>());
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        typeof(FieldManager.Factory).GetField("<MapMetadata>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, test.MapMetadata);
        typeof(FieldManager.Factory).GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, Serilog.Log.Logger);
        typeof(FieldManager).GetField("<FieldFactory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Field, factory);

        Use(item, "");

        Assert.That(session.Item.Inventory.Get(item.Uid), Is.Null);
        string[] sent = Sent();
        Assert.That(sent, Does.Contain(Hex(PlayerHostPacket.StartMiniGame(session.PlayerName, 81000001))));
        Assert.That(sent.Last(), Is.EqualTo(Hex(FieldEnterPacket.Error(MigrationError.s_move_err_default))));
    }

    // 20600001 places billboard interact 19300001 for a day from the dialog's title'description'public text, and is
    // used up; the dialog's banner gets the success.
    [Test]
    public void InstallBillBoard_PlacesTheBoardAndIsUsedUp() {
        var metadata = new InteractObjectMetadata(19300001, InteractType.BillBoard, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0,
            WeaponItemId: 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
        Item item = Login(20600001, ItemFunction.InstallBillBoard,
            "<v interactID=\"19300001\" model=\"MS2InteractMesh\" normal=\"Interaction_advertisement_A01\" " +
            "reactable=\"Interaction_advertisement_A01\" durationSec=\"86400\" />",
            setup: () => test.SetTable(new InteractObjectTable(new Dictionary<int, InteractObjectMetadata> { [19300001] = metadata })));

        Use(item, "Title'Text'0");

        Assert.That(session.Item.Inventory.Get(item.Uid), Is.Null);
        Assert.That(Sent().Last(), Is.EqualTo(Hex(PlayerHostPacket.BillboardInstallResult(BillboardInstallResult.Success))));
    }

    // 20301023 (ItemRePackingScroll 10000001): the client sends the use once its repack window is closed
    // (itemfunctiondispatch ItemRePackingScroll) and the server opens the window for the scroll, which stays.
    [Test]
    public void ItemRePackingScroll_OpensTheWindowAndKeepsTheScroll() {
        Item scroll = Login(20301023, ItemFunction.ItemRePackingScroll, "10000001");

        Use(scroll);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ItemRepackPacket.Open(scroll.Uid)) }));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(1));
    }

    // 20300662 (VIPCoupon, period 168 hours): VIPCoupon has no client use function, so the use goes out as is. One
    // coupon is used up and starts a VIP time of 168 hours with the new-VIP notice.
    [Test]
    public void VIPCoupon_StartsTheVipTimeAndIsUsedUp() {
        Item coupon = Login(20300662, ItemFunction.VIPCoupon, "<v period=\"168\" />", amount: 2);
        // The test session's ConfigManager is bare; the VIP time reads its session.
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
        long before = DateTimeOffset.Now.ToUnixTimeSeconds();

        Use(coupon);

        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(1));
        long premium = session.Player.Value.Account.PremiumTime;
        Assert.That(premium - before, Is.InRange(168 * 3600 - 5, 168 * 3600 + 5));
        Assert.That(session.Player.Value.Character.PremiumTime, Is.EqualTo(premium));
        string[] sent = Sent();
        Assert.That(sent, Does.Contain(Hex(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_vip_coupon_new_msg))));
        Assert.That(sent, Does.Contain(Hex(PremiumCubPacket.Activate(session.Player.ObjectId, premium))));
    }

    // 20301093 (ItemActiveCodiTab 30): opens the second outfit tab for 30 days and is used up.
    [Test]
    public void ItemActiveCodiTab_OpensTheTabForItsDaysAndIsUsedUp() {
        Item item = Login(20301093, ItemFunction.ItemActiveCodiTab, "30", amount: 2);

        Use(item);

        Character character = session.Player.Value.Character;
        Assert.That(character.SkinTabEnd - character.SkinTabStart, Is.EqualTo(30L * SkinTabPeriod.SecondsPerDay));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(Sent(), Does.Contain(Hex(ItemUsePacket.ActiveCodiTab(session.Player.ObjectId, character.SkinTabStart, character.SkinTabEnd,
            30L * SkinTabPeriod.SecondsPerDay))));
    }

    // 20300524 is itself a LulluKey. The client's dialog sends the use on the key with "single" or "multi", plus
    // "autoPay" when its pay box is ticked (UIItemLulluBoxDialog::SendOpenBox). With no LulluBox and no meso for the
    // price the box does not open, the key stays and the dialog gets the error.
    [Test]
    public void OpenItemBoxLullu_WithoutABoxAnswersTheErrorAndKeepsTheKey() {
        Item key = Login(20300524, ItemFunction.OpenItemBoxLullu,
            "<v commonBoxId=\"330000961\" unCommonBoxId=\"330000962\" keyItemTag=\"LulluKey\" boxItemTag=\"LulluBox\" " +
            "boxPrice=\"1000\" meratMarketPopup=\"key\"/>", tag: ItemTag.LulluKey);

        Use(key, "single");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ItemBoxPacket.Open(key.Id, 0, ItemBoxError.s_err_cannot_open_multi_itembox_inventory)) }));

        Use(key, "singleautoPay");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ItemBoxPacket.Open(key.Id, 0, ItemBoxError.s_err_cannot_open_multi_itembox_inventory)) }));
        Assert.That(session.Item.Inventory.Get(key.Uid)?.Amount, Is.EqualTo(1));
    }

    // 30001093 (CreateReactor 10000000) places KR reactor 10000000 (event 900051) at the player and is used up; the
    // client checks the event's fields and count first (CanCreateReactor) and sends a plain use. The event's field
    // and count here are the test's.
    [Test]
    public void CreateReactor_PlacesTheReactorAndIsUsedUp() {
        var reactor = new ReactorTable.Entry(10000000, 155, 1500, 900051, [
            new ReactorTable.State(100, ReactorStateType.Connect, 0, 60, [new ReactorTable.ConditionItem(30001095, 1, 1, 0)]),
            new ReactorTable.State(101, ReactorStateType.Sleep, 15, 30, []),
            new ReactorTable.State(102, ReactorStateType.Reward, 240, 240, []),
            new ReactorTable.State(103, ReactorStateType.Reward, 1, 1, []),
        ], [new ReactorTable.Reward(30001097, 1, 1)]);
        Item item = Login(30001093, ItemFunction.CreateReactor, "10000000", amount: 2, setup: () => {
            test.AddEvent(900051, GameEventType.reactor, new Reactor([2000062], 3));
            test.SetTable(new ReactorTable(new Dictionary<int, ReactorTable.Entry> { [10000000] = reactor }));
        });
        // The test field is bare: what a placement reads, empty, on one flat poly around the player.
        typeof(FieldManager).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Field, TriggerHallGameTests.FlatNavigation());
        foreach (string name in new[] { "reactorAddLock", "fieldReactors", "fieldInteracts", "fieldFunctionInteracts", "fieldAdBalloons", "fieldChests", "fieldPortals" }) {
            FieldInfo field = typeof(FieldManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (field.GetValue(session.Field) == null) {
                field.SetValue(session.Field, Activator.CreateInstance(field.FieldType));
            }
        }

        Use(item);

        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(Sent().Last(), Is.EqualTo(Hex(ReactorPacket.StartReact(10000000))));
    }
}
