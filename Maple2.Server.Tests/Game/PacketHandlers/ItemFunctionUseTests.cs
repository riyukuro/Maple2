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
using Maple2.Server.Tests.Game;
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

    // The test field is made bare: fills the named members it leaves null.
    private void FillField(params string[] names) {
        foreach (string name in names) {
            FieldInfo field = typeof(FieldManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (field.GetValue(session.Field) == null) {
                field.SetValue(session.Field, Activator.CreateInstance(field.FieldType));
            }
        }
    }

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
        // A name of its own: the navmesh file sits in the shared build output, where another test run may delete it.
        string xBlock = $"item_air_taxi_test_{Guid.NewGuid():N}";
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

    // 20300052 (ExpendCharacterSlot): the client sends the use as is. One coupon opens one more character slot and is
    // used up; at the server's most slots (8) the coupon stays and the client gets the most-slots answer.
    [TestCase(4, 5, 1)]
    [TestCase(Constant.ServerMaxCharacters, Constant.ServerMaxCharacters, 2)]
    public void ExpendCharacterSlot_AddsOneSlotUpToTheMost(int slots, int after, int left) {
        Item coupon = Login(20300052, ItemFunction.ExpendCharacterSlot, "", amount: 2);
        session.Player.Value.Account.MaxCharacters = slots;

        Use(coupon);

        Assert.That(session.Player.Value.Account.MaxCharacters, Is.EqualTo(after));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(left));
        Assert.That(Sent().Last(), Is.EqualTo(Hex(slots < after ? ItemUsePacket.CharacterSlotAdded() : ItemUsePacket.MaxCharacterSlots())));
    }

    // 20300964 (HongBao: 50 of 90000020, red merets, for 6 users over 300 s): the client sends the use as is. One
    // envelope is used up, the opener takes the first share (at least 1 of the 50) and the field is told.
    [Test]
    public void HongBao_GivesTheOpenerAShareAndIsUsedUp() {
        Item envelope = Login(20300964, ItemFunction.HongBao,
            "<v itemId=\"90000020\" totalCount=\"50\" totalUser=\"6\" durationSec=\"300\" />", amount: 2,
            setup: () => test.Register(TestGameSession.Metadata(90000020)));
        FillField("hongBaos");
        long before = session.Currency.Meret;

        Use(envelope);

        Assert.That(session.Item.Inventory.Get(envelope.Uid)?.Amount, Is.EqualTo(1));
        long share = session.Currency.Meret - before;
        Assert.That(share, Is.InRange(1, 50 - 5));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.PlayerHost));
    }

    // The same envelope with no items or no users is kept and gives nothing; it gave the opener a share of 0.
    [TestCase(0, 6)]
    [TestCase(50, 0)]
    public void HongBao_NoItemsOrNoUsers_KeepsTheEnvelope(int totalCount, int totalUser) {
        Item envelope = Login(20300964, ItemFunction.HongBao,
            $"<v itemId=\"90000020\" totalCount=\"{totalCount}\" totalUser=\"{totalUser}\" durationSec=\"300\" />", amount: 2,
            setup: () => test.Register(TestGameSession.Metadata(90000020)));
        FillField("hongBaos");
        test.TakeSentOps();

        Use(envelope);

        Assert.That(session.Item.Inventory.Get(envelope.Uid)?.Amount, Is.EqualTo(2));
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.PlayerHost));
    }

    // 20300012 (AddAdditionalEffect 70000029,1; the effect lasts 600 s): the client sends the use as is. The effect
    // starts on the user and one item is used up.
    [Test]
    public void AddAdditionalEffect_StartsTheEffectAndIsUsedUp() {
        Item item = Login(20300012, ItemFunction.AddAdditionalEffect, "70000029,1", amount: 2);
        EffectField(BuffAttributeTests.Effect(70000029, DisabledBuffTests.Property with { DurationTick = 600000 }));

        Use(item);

        Assert.That(session.Player.Buffs.EnumerateBuffs(70000029), Has.Count.EqualTo(1));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
    }

    // The same item when the field's data has no effect 70000029: nothing starts and the item is kept.
    [Test]
    public void AddAdditionalEffect_AnEffectNotInTheData_KeepsTheItem() {
        Item item = Login(20300012, ItemFunction.AddAdditionalEffect, "70000029,1", amount: 2);
        EffectField();

        Use(item);

        Assert.That(session.Player.Buffs.EnumerateBuffs(70000029), Is.Empty);
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(2));
    }

    // The same item while the character holds an effect immune to 70000029 (immuneEffectCodes, as 10100051 is to
    // 10100052 in KR): nothing starts and the item is kept.
    [Test]
    public void AddAdditionalEffect_WhileImmune_KeepsTheItem() {
        Item item = Login(20300012, ItemFunction.AddAdditionalEffect, "70000029,1", amount: 2);
        EffectField(BuffAttributeTests.Effect(70000029, DisabledBuffTests.Property with { DurationTick = 600000 }),
            BuffAttributeTests.Effect(10100051, DisabledBuffTests.Property with { DurationTick = 600000 }) with {
                Update = new AdditionalEffectMetadataUpdate(null, [70000029], [], [], []),
            });
        Assert.That(session.Player.Buffs.AddBuff(session.Player, session.Player, 10100051, 1, session.Field!.FieldTick), Is.True);

        Use(item);

        Assert.That(session.Player.Buffs.EnumerateBuffs(70000029), Is.Empty);
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(2));
    }

    // 30001099 (SurvivalScan "Position Tracker", `<v duration="10000" isAnnounce="false"/>`; SurvivalScanAdditionalID is
    // 71000052 in KR, a 10 s buff): the client sends the plain use. In a running Maple Survival game the effect starts on
    // the user for the item's 10 s and one item is used up.
    [Test]
    public void SurvivalScan_InARunningGame_StartsTheEffectAndIsUsedUp() {
        Item item = ScanItem();
        SurvivalGame();

        Use(item);

        Assert.That(session.Player.Buffs.EnumerateBuffs(ScanEffect).Select(buff => buff.EndTick - buff.StartTick), Is.EqualTo(new[] { 10000L }));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
    }

    // Outside a game, after the game's end, with SurvivalContents01 off, when the user has fallen or when the data has
    // no effect: nothing starts and the item is kept.
    [TestCase("none")]
    [TestCase("ended")]
    [TestCase("off")]
    [TestCase("fallen")]
    [TestCase("noEffect")]
    public void SurvivalScan_Refused_KeepsTheItem(string reason) {
        Item item = ScanItem(reason == "off");
        if (reason == "noEffect") {
            EffectField();
        }
        if (reason != "none") {
            SurvivalGame(withEffect: reason != "noEffect");
        }
        if (reason == "ended") {
            // The other two leave: the user is the last one standing and the game ends.
            foreach (FieldPlayer player in session.Field!.Players.Values.Where(player => player != session.Player).ToList()) {
                session.Field.Survival!.Leave(player);
            }
            Assume.That(session.Field.Survival!.Ended, Is.True);
        }
        if (reason == "fallen") {
            // The user falls; the other two still play, so the game runs on.
            session.Field!.Survival!.Leave(session.Player);
            Assume.That(session.Field.Survival.Ended || !session.Field.Survival.InRunningGame(session.CharacterId), Is.False);
        }

        Use(item);

        Assert.That(session.Player.Buffs.EnumerateBuffs(ScanEffect), Is.Empty);
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(2));
    }

    private const int ScanEffect = 71000052;

    private Item ScanItem(bool featureOff = false) {
        return Login(30001099, ItemFunction.SurvivalScan, "<v duration=\"10000\" isAnnounce=\"false\" />", amount: 2, setup: () => {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { SurvivalScanAdditionalID = ScanEffect });
            if (featureOff) {
                test.SetFeaturesOff("SurvivalContents01");
            }
        });
    }

    // Two more players join the user's field and the survival game starts with the three; the field knows the scan's
    // effect.
    private void SurvivalGame(bool withEffect = true) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (withEffect) {
            EffectField(BuffAttributeTests.Effect(ScanEffect, DisabledBuffTests.Property with { DurationTick = 10000 }));
        }
        FieldManager field = session.Field!;
        for (int i = 1; i <= 2; i++) {
            var other = new TestGameSession(test.Options);
            other.Start();
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", any)!.SetValue(other.Session.Player, session.Player.ObjectId + i);
            typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(other.Session.Player, field);
            other.Session.Field = field;
            field.Players[other.Session.Player.ObjectId] = other.Session.Player;
        }
        // The test field has no constructor run: the game's end looks up the storm widget in this table.
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(field, new Dictionary<string, Maple2.Server.Game.Model.Widget.Widget>());
        field.StartSurvival();
        Assume.That(field.Survival!.Playing(session.CharacterId), Is.True);
    }

    // The field knows the given effects at level 1 (as BuffAttributeTests sets it up), the player stands in the field.
    private void EffectField(params AdditionalEffectMetadata[] effects) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var skills = new SkillMetadataStorage(new Maple2.Database.Context.MetadataContext(
            new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", any)!.GetValue(skills)!;
        foreach (AdditionalEffectMetadata effect in effects) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(effect.Id, (short) 1), effect]);
        }
        FieldManager field = session.Field!;
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(session.Player, field);
    }

    // 20302629 (OpenItemBoxLimitedQuantity: box 340000326, 3 s cooldown; 30001369 is its common item). The client
    // keeps the same cooldown per item id (OpenItemBoxLimitedQuantity @0x140d25c50) and then sends the use with no
    // text. The first use opens one box; a second within the cooldown opens none.
    [Test]
    public void OpenItemBoxLimitedQuantity_OpensOneBoxThenWaitsForTheCooldown() {
        Item box = Login(20302629, ItemFunction.OpenItemBoxLimitedQuantity,
            "<xmlData individualDropBoxId=\"340000326\" cooldownTime=\"3\" />", amount: 3, setup: () => {
                test.Register(TestGameSession.Metadata(30001369));
                test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
                    new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
                test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
                    [340000326] = TestGameSession.DropBox([(30001369, 1)]),
                }));
            });

        Use(box);

        Assert.That(session.Item.Inventory.Get(box.Uid)?.Amount, Is.EqualTo(2));
        Assert.That(session.Item.Inventory.Filter(item => item.Id == 30001369).Sum(item => item.Amount), Is.EqualTo(1));
        Assert.That(Sent(), Does.Contain(Hex(ItemBoxPacket.Open(20302629, 1))));

        Use(box);

        Assert.That(session.Item.Inventory.Get(box.Uid)?.Amount, Is.EqualTo(2));
        Assert.That(Sent(), Is.Empty);
    }

    private static MapMetadata RecallField(bool recall) => TestGameSession.Map(2000062) with {
        CashCall = new MapMetadataCashCall(false, false, false, false, recall),
    };

    private static PlayerInfo Info(long characterId, short channel) =>
        new(new CharacterInfo(1, characterId, $"m{characterId}", "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []) {
            Channel = channel,
            MapId = 2000062,
        };

    // 20300053 (RecallParty): the party window's recall button (UIPartyDialog onPartyRecall) casts StateRecall, then
    // sends the plain use. With another member and a field that allows recalls the World is asked to call the party
    // here and one scroll is used up; a field without recalls asks nothing and keeps it.
    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void RecallParty_CallsThePartyHereAndIsUsedUp(bool recall, int left) {
        Item scroll = Login(20300053, ItemFunction.RecallParty, "", amount: 2, field: RecallField(recall));
        var party = new Maple2.Model.Game.Party.Party(5, new Maple2.Model.Game.Party.PartyMember { PartyId = 5, Info = Info(test.CharacterId, 1) });
        party.Members[test.CharacterId] = new Maple2.Model.Game.Party.PartyMember { PartyId = 5, Info = Info(test.CharacterId, 1) };
        party.Members[77] = new Maple2.Model.Game.Party.PartyMember { PartyId = 5, Info = Info(77, 2) };
        session.Party = new Maple2.Server.Game.Manager.PartyManager(test.World, session);
        typeof(Maple2.Server.Game.Manager.PartyManager).GetField("<Party>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Party, party);
        var asked = new List<Maple2.Server.World.Service.PartyRequest>();
        test.World.PartyWorld = request => {
            asked.Add(request);
            return new Maple2.Server.World.Service.PartyResponse();
        };

        Use(scroll);

        Assert.That(asked.Select(request => (request.Recall.PartyId, request.Recall.MapId)),
            Is.EqualTo(recall ? new[] { (5, 2000062) } : Array.Empty<(int, int)>()));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(left));
    }

    // 20300054 (RecallGuild): the client casts StateRecall, then sends the plain use. With a member online outside a
    // tutorial the World is asked to call the guild here and one scroll is used up; with only the caller online
    // (the client counts the same, CGuildInfo::FUN_1404d3400) nothing is asked and the scroll stays.
    [TestCase((short) 2, 1)]
    [TestCase((short) -1, 2)]
    public void RecallGuild_CallsTheGuildHereAndIsUsedUp(short otherChannel, int left) {
        Item scroll = Login(20300054, ItemFunction.RecallGuild, "", amount: 2, field: RecallField(true),
            setup: () => test.AddMap(TestGameSession.Map(2000062)));
        var guild = new Guild(9, "g", test.AccountId, test.CharacterId, session.PlayerName);
        guild.Members[test.CharacterId] = new GuildMember { GuildId = 9, Info = Info(test.CharacterId, 1) };
        guild.Members[77] = new GuildMember { GuildId = 9, Info = Info(77, otherChannel) };
        typeof(Maple2.Server.Game.Manager.GuildManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Guild, session);
        typeof(Maple2.Server.Game.Manager.GuildManager).GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Guild, Serilog.Log.Logger);
        typeof(Maple2.Server.Game.Manager.GuildManager).GetField("<Guild>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Guild, guild);

        Use(scroll);

        Assert.That(test.World.GuildRequests.Select(request => (request.Recall.GuildId, request.Recall.MapId)),
            Is.EqualTo(left == 1 ? new[] { (9L, 2000062) } : Array.Empty<(long, int)>()));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(left));
    }

    // KR 35200000 used outside the player's own indoor home: the server refuses it and keeps the item (the client
    // sends the use from its s_msg_item_open_blueprint box, so the home check is the server's).
    [Test]
    public void BlueprintImport_OutsideTheHomeIsRefusedAndKeepsTheItem() {
        Item blueprint = Login(35200000, ItemFunction.BlueprintImport, "");
        session.Item.Inventory.Get(blueprint.Uid)!.Blueprint = new ItemBlueprint { BlueprintUid = 1 };

        Use(blueprint);

        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(NoticePacket.Message(StringCode.s_ugcmap_not_use_blueprint_item, NoticePacket.Flags.Message | NoticePacket.Flags.Alert)),
        }));
        Assert.That(session.Item.Inventory.Get(blueprint.Uid)?.Amount, Is.EqualTo(1));
    }

    // 20303165 (RecallWedding): the client sends the plain use. A married player whose partner is online asks the
    // World to call the partner here and uses one scroll up; an engaged couple (CanRecall) asks nothing and keeps it.
    [TestCase(MaritalStatus.Married, 1)]
    [TestCase(MaritalStatus.Engaged, 2)]
    public void RecallWedding_CallsThePartnerHereAndIsUsedUp(MaritalStatus status, int left) {
        Item scroll = Login(20303165, ItemFunction.RecallWedding, "", amount: 2, field: RecallField(true));
        test.World.AddPlayer(2, 77, "m77");
        test.World.Players[77].Channel = 2;
        session.Marriage.Marriage = new Marriage {
            Id = 1, Status = status, Partner1 = new MarriagePartner { Info = Info(test.CharacterId, 1) },
            Partner2 = new MarriagePartner { Info = Info(77, 2) },
        };

        Use(scroll);

        Assert.That(test.World.MarriageRequests.Select(request => (request.ReceiverId, request.Recall.MapId)),
            Is.EqualTo(left == 1 ? new[] { (77L, 2000062) } : Array.Empty<(long, int)>()));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)?.Amount, Is.EqualTo(left));
    }

    // 20001703 (WeddingExpItem, a Blessing Charm): the client sends the plain use. A married player's charm adds the
    // reward table's weddingExpItem exp to the marriage and is used up; a second charm inside the month limit is kept.
    [Test]
    public void WeddingExpItem_AddsTheMarriageExpOnceAMonth() {
        Item charm = Login(20001703, ItemFunction.WeddingExpItem, "", amount: 2, setup: () => test.SetTable(new WeddingTable(
            new Dictionary<MarriageExpType, WeddingReward> {
                [MarriageExpType.weddingExpItem] = new WeddingReward(MarriageExpType.weddingExpItem, 350, MarriageExpLimit.month),
            }, new Dictionary<int, WeddingPackage>())));
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            Marriage marriage = db.CreateMarriage(test.CharacterId, partnerId)!;
            Assert.That(db.SetMarried(marriage.Id, DateTime.Now), Is.True);
            session.Marriage.Marriage = db.GetMarriage(test.CharacterId)!;
        }

        Use(charm);

        Assert.That(session.Marriage.Marriage.Exp, Is.EqualTo(350));
        Assert.That(session.Item.Inventory.Get(charm.Uid)?.Amount, Is.EqualTo(1));

        Use(charm);

        Assert.That(session.Marriage.Marriage.Exp, Is.EqualTo(350));
        Assert.That(session.Item.Inventory.Get(charm.Uid)?.Amount, Is.EqualTo(1));
    }
}
