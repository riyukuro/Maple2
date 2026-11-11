using System;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

public class NoRowOpcodeLayoutTests {
    // Client: WorldMapManager::LoadIconPacket (guess/worldmapmanager.cpp:646): boss list, byte second list, then a
    // byte that must be 1 for LoadAddtionalInfos (byte 3, int count, count x (int, int, short)) to be read.
    [Test]
    public void WorldMapLoadHasPopulationFlag() {
        ByteWriter writer = WorldMapPacket.Load([], [new MapPopulation { MapId = 2000023, Population = 7, Channel = 2 }]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Worldmap));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // boss list present
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0)); // no second list
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // population block follows
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2000023)); // map id
        Assert.That(reader.ReadInt(), Is.EqualTo(7)); // population
        Assert.That(reader.ReadShort(), Is.EqualTo(2)); // channel
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4 + 1 + 1 + 1 + 4 + 10));
    }

    // Client: UIItemRemakeOptionDialog::vfunc_79 (unnamed/unit_14129cfa0_itemremakeoptiondialog.cpp:324): kind 5 is
    // OnRemakeError (:1154): byte flag, int code.
    [Test]
    public void ChangeAttributesErrorIsKind5() {
        ByteWriter writer = ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_lack_price);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4));
    }

    // Client: UIItemExtractionScrollDialog::vfunc_79 (uiitemextractionscrolldialog.cpp:244): byte 0, long source uid,
    // long result uid, short remaining extraction count of the source item.
    [Test]
    public void GlamourAnvilExtractCarriesRemainingCount() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var result = new Item(metadata, 1) { Uid = 77 };

        ByteWriter writer = ItemExtractionPacket.Extract(55, 2, result);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GlamourAnvil));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(55));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadShort(), Is.EqualTo(2));

        // Tail: the result item's colour block (three colours of 4 bytes, int index, int palette), then its
        // transfer block, and nothing after it.
        Assert.That(reader.ReadBytes(20), Is.EqualTo(new byte[20]));
        var transfer = new ByteWriter();
        transfer.WriteClass<ItemTransfer>(result.Transfer ?? ItemTransfer.Default);
        byte[] transferBytes = transfer.Buffer.AsSpan(0, transfer.Length).ToArray();
        Assert.That(transferBytes, Is.Not.Empty);
        Assert.That(reader.ReadBytes(transferBytes.Length), Is.EqualTo(transferBytes));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 8 + 2 + 20 + transferBytes.Length));
    }

    private static Item RemakeItem() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 4) { Uid = 91 };
    }

    // Client: UIItemRemakeOptionDialog::FUN_14129f940 (unnamed/unit_14129cfa0_itemremakeoptiondialog.cpp:626):
    // kind 4 is long item uid, then CIngredientContainer (commonfiles/commonstruct_wa.cpp:255): byte count,
    // count x (int item id, int tag, int count).
    [Test]
    public void ChangeAttributesStageItemListsIngredients() {
        ByteWriter writer = ChangeAttributesPacket.StageItem(91, [
            new IngredientInfo(ItemTag.GreenCrystal, 3),
            new IngredientInfo(ItemTag.ChaosOnix, 120),
        ]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(91));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) ItemTag.GreenCrystal));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) ItemTag.ChaosOnix));
        Assert.That(reader.ReadInt(), Is.EqualTo(120));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 1 + 2 * 12));
    }

    // Client: UIItemRemakeOptionDialog::FUN_1412a2c50 (:1069): kind 6 is byte result. 0: nothing more. 2: int
    // item id. Otherwise: int item id, long item uid, then the preview block (long item uid and the item).
    [Test]
    public void ChangeAttributesPendingHasThreeForms() {
        ByteWriter none = ChangeAttributesPacket.NoPending();
        var reader = new ByteReader(none.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(none.Length, Is.EqualTo(2 + 1 + 1));

        ByteWriter gone = ChangeAttributesPacket.PendingItemGone(11300001);
        reader = new ByteReader(gone.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001));
        Assert.That(gone.Length, Is.EqualTo(2 + 1 + 1 + 4));

        Item item = RemakeItem();
        ByteWriter pending = ChangeAttributesPacket.PendingItem(item);
        ByteWriter preview = ChangeAttributesPacket.PreviewItem(item);
        reader = new ByteReader(pending.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001));
        Assert.That(reader.ReadLong(), Is.EqualTo(91));
        // The rest is the body of the preview packet (kind 1) after its opcode and kind byte.
        byte[] previewBody = preview.Buffer.AsSpan(3, preview.Length - 3).ToArray();
        Assert.That(previewBody.Length, Is.GreaterThan(8));
        Assert.That(reader.ReadBytes(previewBody.Length), Is.EqualTo(previewBody));
        Assert.That(pending.Length, Is.EqualTo(2 + 1 + 1 + 4 + 8 + previewBody.Length));
    }

    // Client: CSceneStateGame::OnStateFishingData (scenestategame.cpp:8510): int player object id, int state,
    // int sub state. Fishing is state 45, sub state 0 (fishingmanager.cpp:1306); leaving it is state 1 (:1336).
    [Test]
    public void StateFishingDataIsThreeInts() {
        ByteWriter writer = StateFishingPacket.State(1234, StateFishingPacket.STATE_FISHING);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.StateFishingData));
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.ReadInt(), Is.EqualTo(45));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 4 + 4 + 4));

        writer = StateFishingPacket.State(1234, StateFishingPacket.STATE_IDLE);
        reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.StateFishingData));
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
    }

    // Client: UIItemExchangeScrollDialog::UpdateIngredientCounts (uiitemexchangescrolldialog.cpp:518): kind 3
    // has no fields.
    [Test]
    public void ItemExchangeRefreshHasNoFields() {
        ByteWriter writer = ItemExchangeScrollPacket.RefreshIngredients();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemExchange));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(writer.Length, Is.EqualTo(2 + 1));
    }

    // Client: CSceneStateGame::OnAutoRevive (scenestategame.cpp:4332): one int, the countdown in milliseconds.
    [Test]
    public void AutoReviveCountdownIsOneInt() {
        ByteWriter writer = AutoRevivePacket.Countdown(10000);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.AutoRevive));
        Assert.That(reader.ReadInt(), Is.EqualTo(10000));
        Assert.That(writer.Length, Is.EqualTo(2 + 4));
    }

    // Client: CSceneStateGame::OnDungeonReviveCount (scenestategame.cpp:5074): one int, the revivals used.
    [Test]
    public void DungeonReviveCountIsOneInt() {
        ByteWriter writer = RevivalPacket.DungeonReviveCount(2);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DungeonReviveCount));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(writer.Length, Is.EqualTo(2 + 4));
    }

    // The field xml's autoRevivalTime is seconds (the data holds 1, 3, 8, 9 and 20); the packet's int is milliseconds
    // (UIAutoReviveCountdownDialog, uiautorevivecountdowndialog.cpp:78).
    [Test]
    public void AutoReviveDelayIsFieldSecondsInMs() {
        MapMetadataProperty Property(AutoReviveType type, int time) => new(Continent: default, Region: default,
            Category: 0, Type: default, BigCity: false, ExploreType: 0, TutorialType: 0, RevivalReturnId: 0,
            EnterReturnId: 0, AutoRevivalType: type, AutoRevivalTime: time, InfiniteMeretRevival: false,
            NoRevivalHere: false, ReviveFullHp: false, UseTimeEvent: false, HomeReturnable: false,
            DeathPenalty: false, OnlyDarkTomb: false, PkMode: false, CanFly: false, CanClimb: false, IndoorType: 0);

        Assert.That(FieldPlayer.AutoReviveDelayMs(Property(AutoReviveType.Countdown, 20)), Is.EqualTo(20000));
        Assert.That(FieldPlayer.AutoReviveDelayMs(Property(AutoReviveType.Trigger, 3)), Is.EqualTo(3000));
        Assert.That(FieldPlayer.AutoReviveDelayMs(Property(AutoReviveType.None, 3)), Is.EqualTo(0));
        Assert.That(FieldPlayer.AutoReviveDelayMs(Property(AutoReviveType.Countdown, 0)), Is.EqualTo(0));
    }

    // Client: CMeratMarketMan::OnSaleNoticeTimes (meratmarketman.cpp:2454): command 26, long sales total, long.
    [Test]
    public void MeretMarketSalesTotalIsTwoLongs() {
        ByteWriter writer = MeretMarketPacket.UpdateProfit(4500);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MeretMarket));
        Assert.That(reader.ReadByte(), Is.EqualTo(26));
        Assert.That(reader.ReadLong(), Is.EqualTo(4500));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 8));
    }

    // Client: CMeratMarketMan::OnMySaleProducts (meratmarketman.cpp:2211): command 11, long sales total, int count.
    [Test]
    public void MeretMarketOwnListingsStartWithSalesTotal() {
        ByteWriter writer = MeretMarketPacket.LoadPersonalListings([], 4500);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MeretMarket));
        Assert.That(reader.ReadByte(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(4500));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 4));
    }
}
