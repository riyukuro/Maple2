using Google.Protobuf;
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
        Assert.That(reader.ReadInt(), Is.EqualTo(94));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(98));
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

    // Client: UIItemExchangeScrollDialog::FUN_140f52ca0 (uiitemexchangescrolldialog.cpp:482): kind 0 is long
    // scroll uid, long target uid, byte count, count x (int item id, int tag, int count).
    [Test]
    public void ItemExchangeSetTargetListsIngredients() {
        ItemComponent[] ingredients = [new ItemComponent(30000001, 1, 5, ItemTag.None), new ItemComponent(0, 1, 2, ItemTag.SkinCrystal)];
        ByteWriter writer = ItemExchangeScrollPacket.SetTarget(11L, 22L, ingredients);
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemExchange));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(11L));
        Assert.That(reader.ReadLong(), Is.EqualTo(22L));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(30000001));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) ItemTag.None));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(88));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Client: UIItemRemakeOptionDialog::FUN_1412a3460 (unit_14129cfa0_itemremakeoptiondialog.cpp:1142): kind 8
    // has no fields.
    [Test]
    public void ChangeAttributesRefreshCostHasNoFields() {
        Assert.That(ChangeAttributesPacket.RefreshCost().ToArray(), Is.EqualTo(new byte[] { (byte) SendOp.ChangeAttributes, 0x00, 8 }));
    }

    // A player entering a field is sent state 45 for each player whose guide object is the fishing one.
    [Test]
    public void FishingStateOnEntryIsForTheFishingGuideOnly() {
        Assert.That(StateFishingPacket.IsFishing(GuideObjectType.Fishing), Is.True);
        Assert.That(StateFishingPacket.IsFishing(GuideObjectType.Construction), Is.False);
        Assert.That(StateFishingPacket.State(7, StateFishingPacket.STATE_FISHING).ToArray().AsSpan(2).ToArray(),
            Is.EqualTo(new byte[] { 7, 0, 0, 0, 45, 0, 0, 0, 0, 0, 0, 0 }));
    }

    // Client: CSceneStateGame::OnRecallUser (scenestategame.cpp:4733): kind 4 = byte type, name, int invite id;
    // kinds 5 and 7 = name, byte result; kind 6 = name.
    [Test]
    public void RecallUserKinds() {
        var reader = new ByteReader(RecallUserPacket.Invite("Ab", 77).Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.RecallUser));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ab"));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));

        Assert.That(RecallUserPacket.Requested("A", RecallUserPacket.REQUEST_CONFIRM).ToArray().AsSpan(2).ToArray(),
            Is.EqualTo(new byte[] { 5, 1, 0, (byte) 'A', 0, 1 }));
        Assert.That(RecallUserPacket.Recalled("A").ToArray().AsSpan(2).ToArray(), Is.EqualTo(new byte[] { 6, 1, 0, (byte) 'A', 0 }));
        Assert.That(RecallUserPacket.Fail("A", RecallUserPacket.FAIL_BUSY).ToArray().AsSpan(2).ToArray(),
            Is.EqualTo(new byte[] { 7, 1, 0, (byte) 'A', 0, 4 }));
        // The client has own messages for 2, 4 and 6; reject must be none of them.
        Assert.That(new[] { RecallUserPacket.FAIL_CANNOT_MOVE, RecallUserPacket.FAIL_BUSY, RecallUserPacket.FAIL_NOT_FOUND }, Is.EqualTo(new byte[] { 2, 4, 6 }));
        Assert.That(RecallUserPacket.FAIL_REJECT, Is.Not.AnyOf(2, 4, 6));
    }

    // c2s RecallUser 4 (CNetworkService::SendRecallUserReply): the answer counts only for the pending invite id, in time.
    [Test]
    public void RecallUserAnswerMustMatchThePendingInvite() {
        var pending = new PendingUserRecall(5, 1, "A", 2000001, 100);
        Assert.That(PendingUserRecall.Matches(pending, 5, 100), Is.True);
        Assert.That(PendingUserRecall.Matches(pending, 6, 100), Is.False);
        Assert.That(PendingUserRecall.Matches(pending, 5, 101), Is.False);
        Assert.That(PendingUserRecall.Matches(null, 5, 0), Is.False);
        Assert.That(PendingUserRecall.NextInviteId(), Is.Not.EqualTo(0));
    }

    // A second invite is refused while one is pending (the client would show no box for it).
    [Test]
    public void RecallUserInviteWaitsForThePendingOne() {
        var pending = new PendingUserRecall(5, 1, "A", 2000001, 100);
        Assert.That(PendingUserRecall.CanInvite(null, 0), Is.True);
        Assert.That(PendingUserRecall.CanInvite(pending, 100), Is.False);
        Assert.That(PendingUserRecall.CanInvite(pending, 101), Is.True);
    }

    // The result a caller on another channel gets through the World server is the same packet as on one channel.
    [Test]
    public void RecallUserResultByRelay() {
        Assert.That(RecallUserPacket.Result("A", true, RecallUserPacket.FAIL_BUSY).ToArray(),
            Is.EqualTo(RecallUserPacket.Requested("A", RecallUserPacket.REQUEST_RECALL).ToArray()));
        Assert.That(RecallUserPacket.Result("A", false, RecallUserPacket.FAIL_CANNOT_MOVE).ToArray(),
            Is.EqualTo(RecallUserPacket.Fail("A", RecallUserPacket.FAIL_CANNOT_MOVE).ToArray()));

        // The relay messages keep their fields through the wire format.
        var invite = new PlayerWarpRequest {
            RequesterId = 11,
            RecallInvite = new PlayerWarpRequest.Types.RecallInvite { TargetId = 22, CallerName = "A", MapId = 2000001 },
        };
        PlayerWarpRequest parsed = PlayerWarpRequest.Parser.ParseFrom(invite.ToByteArray());
        Assert.That(parsed.RequestCase, Is.EqualTo(PlayerWarpRequest.RequestOneofCase.RecallInvite));
        Assert.That((parsed.RequesterId, parsed.RecallInvite.TargetId, parsed.RecallInvite.CallerName, parsed.RecallInvite.MapId), Is.EqualTo((11L, 22L, "A", 2000001)));
    }
}
