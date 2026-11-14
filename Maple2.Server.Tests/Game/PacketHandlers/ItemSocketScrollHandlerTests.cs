using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using BagItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketScroll 1 (UIItemSocketScrollDialog::OnClickAction, uiitemsocketscrolldialog.cpp:520): the target is
// checked as the client checks it (FUN_140627450, itemsocketsystemutil.cpp:294), and the answer is read as the client
// reads it (FUN_140f67650 for an error, FUN_140f672a0 for the result).
public class ItemSocketScrollHandlerTests {
    private const int ScrollItemId = 20300001;
    private const int ScrollId = 10000001;

    private TestGameSession test = null!;
    private BagItem gear = null!;
    private BagItem scroll = null!;

    private GameSession Start(int skinType = 0, int rarity = 4, int level = 50, int gearId = 11200001, int successRate = 10000,
        AchievementMetadata? trophy = null) {
        test = new TestGameSession();
        if (trophy != null) {
            test.AddAchievement(trophy);
        }
        test.SetTable(new ItemSocketScrollTable(new Dictionary<int, ItemSocketScrollMetadata> {
            [ScrollId] = new(MinLevel: 50, MaxLevel: 50, ItemTypes: [12], Rarities: [4], SocketCount: 1, TradableCountDeduction: 0,
                SuccessRate: successRate),
        }));
        ItemMetadata gearData = TestGameSession.Metadata(gearId, type: 1, slotMax: 1);
        gear = test.Store(gearData with {
            Property = gearData.Property with { SkinType = skinType },
            Limit = gearData.Limit with { Level = level },
        });
        scroll = test.Store(TestGameSession.Metadata(ScrollItemId, type: 1, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.ItemSocketScroll, "ItemSocketScroll", $"{ScrollId}", false)));
        GameSession session = test.Start();
        test.TakeSent();
        gear = session.Item.Inventory.Get(gear.Uid)!;
        gear.Rarity = rarity;
        gear.Socket = new ItemSocket(3, 0);
        scroll = session.Item.Inventory.Get(scroll.Uid)!;
        return session;
    }

    private void Use(GameSession session) {
        new ItemSocketScrollHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(gear.Uid);
            writer.WriteLong(scroll.Uid);
        }));
    }

    // The error the client reads (FUN_140f67650: mode 3, a byte, the code), or null when none was sent.
    private int? SentError() {
        foreach (byte[] packet in test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketScroll)) {
            var reader = new ByteReader(packet, 2);
            if (reader.ReadByte() != 3) {
                continue;
            }
            reader.ReadByte();
            return reader.ReadInt();
        }
        return null;
    }

    private bool ScrollHeld(GameSession session) => session.Item.Inventory.Get(scroll.Uid) != null;

    // Before, an item with a skinType was not checked: the scroll opened its socket though the client refuses it.
    [Test]
    public void SkinTypeItem_IsRefusedWithUsePart() {
        GameSession session = Start(skinType: 1);
        Use(session);
        Assert.Multiple(() => {
            Assert.That(SentError(), Is.EqualTo((int) ItemSocketScrollError.s_itemsocket_scroll_error_impossible_usepart));
            Assert.That(gear.Socket!.UnlockSlots, Is.Zero);
            Assert.That(ScrollHeld(session), Is.True);
        });
    }

    // The first rule that fails is the client's: slot before rank before level. Before, the level came first.
    [Test]
    public void WrongSlotAndLevel_AnswersSlot() {
        GameSession session = Start(level: 60, gearId: 11300001);
        Use(session);
        Assert.That(SentError(), Is.EqualTo((int) ItemSocketScrollError.s_itemsocket_scroll_error_impossible_slot));
    }

    [Test]
    public void WrongRankAndLevel_AnswersRank() {
        GameSession session = Start(rarity: 3, level: 60);
        Use(session);
        Assert.That(SentError(), Is.EqualTo((int) ItemSocketScrollError.s_itemsocket_scroll_error_impossible_rank));
    }

    // The level comes before the socket checks: an item that fits no rule but the socket answers level, not socket.
    [Test]
    public void WrongLevelAndNoSocket_AnswersLevel() {
        GameSession session = Start(level: 60);
        gear.Socket = null;
        Use(session);
        Assert.That(SentError(), Is.EqualTo((int) ItemSocketScrollError.s_itemsocket_scroll_error_impossible_level));
    }

    [Test]
    public void FittingItem_IsOpened() {
        GameSession session = Start();
        Use(session);
        Assert.Multiple(() => {
            Assert.That(SentError(), Is.Null);
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(ScrollHeld(session), Is.False);
        });
    }

    // KR 23200034 (socket_unlock_fail, grades 1, 10, 50): a failed scroll unlock counts; before, nothing sent it.
    [Test]
    public void FailedRoll_CountsSocketUnlockFail() {
        const int trophy = 23200034;
        var metadata = new AchievementMetadata(trophy, $"{trophy}", false, AchievementCategory.Life, [],
            new Dictionary<int, AchievementMetadataGrade> {
                [1] = new(1, new ConditionMetadata(ConditionType.socket_unlock_fail, 1, null, null), null),
                [2] = new(2, new ConditionMetadata(ConditionType.socket_unlock_fail, 10, null, null), null),
            }, false);
        GameSession session = Start(successRate: 0, trophy: metadata);
        Use(session);
        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.TrophyCounter(trophy), Is.EqualTo(1));
    }

    [Test]
    public void OpenedRoll_DoesNotCountSocketUnlockFail() {
        const int trophy = 23200034;
        var metadata = new AchievementMetadata(trophy, $"{trophy}", false, AchievementCategory.Life, [],
            new Dictionary<int, AchievementMetadataGrade> {
                [1] = new(1, new ConditionMetadata(ConditionType.socket_unlock_fail, 1, null, null), null),
            }, false);
        GameSession session = Start(trophy: metadata);
        Use(session);
        Assert.That(test.TrophyCounter(trophy) ?? 0, Is.Zero);
    }

    // A failed roll takes the scroll, opens nothing and answers mode 2 with result 0, the uid and the byte, which is
    // all the client reads for a fail (FUN_14062a230). Before, every unlock opened.
    [Test]
    public void FailedRoll_TakesTheScrollAndAnswersFail() {
        GameSession session = Start(successRate: 0);
        Use(session);
        byte[] answer = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.ItemSocketScroll);
        var reader = new ByteReader(answer, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(gear.Uid));
            reader.ReadByte();
            Assert.That(reader.Available, Is.Zero);
            Assert.That(gear.Socket!.UnlockSlots, Is.Zero);
            Assert.That(ScrollHeld(session), Is.False);
        });
    }

    [TestCase(6999, true)]
    [TestCase(7000, false)]
    public void Roll_IsOutOf10000(int roll, bool opens) {
        var metadata = new ItemSocketScrollMetadata(50, 50, [12], [4], 1, 0, SuccessRate: 7000);
        Assert.That(ItemSocketScrollHandler.Succeeds(metadata, roll), Is.EqualTo(opens));
    }

    // The dialog shows the int after the uid div 100 as the success chance (FUN_140f67170); it was always 10000.
    [Test]
    public void UseScroll_SendsTheScrollsRate() {
        Start();
        var metadata = new ItemSocketScrollMetadata(50, 50, [12], [4], 2, 0, SuccessRate: 5000);
        ByteWriter writer = ItemSocketScrollPacket.UseScroll(scroll, metadata);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(scroll.Uid));
            Assert.That(reader.ReadInt(), Is.EqualTo(5000));
            Assert.That(reader.ReadByte(), Is.EqualTo(2));
        });
    }
}
