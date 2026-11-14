using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using BagItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemExchangeScroll 1 (UIItemExchangeScrollDialog::SendItemExchangeScrollMode1, uiitemexchangescrolldialog.cpp:575):
// the scrolls and the materials of an exchange are counted as the dialog counts them, without locked or unlocking
// items (GetItemCountByFilter with m_extra 1, inventory.cpp:743-766; uiitemexchangescrolldialog.cpp:396-420).
public class ItemExchangeScrollHandlerTests {
    private const int ScrollId = 20000001;
    private const int MaterialId = 30000001;
    private const int RewardId = 30000002;

    private TestGameSession test = null!;

    private GameSession Start(out BagItem lockedScroll, out BagItem freeScroll, out BagItem lockedMaterial, out BagItem freeMaterial, long fee = 0, long meso = 0) {
        test = new TestGameSession();
        test.SetTable(new ItemExchangeScrollTable(new Dictionary<int, ItemExchangeScrollMetadata> {
            [77] = new(new ItemComponent(ScrollId, -1, 1, ItemTag.None), new ItemComponent(RewardId, 1, 1, ItemTag.None), 0, fee,
                [new ItemComponent(MaterialId, -1, 1, ItemTag.None)]),
        }));
        ItemMetadata scroll = TestGameSession.Metadata(ScrollId, type: 1, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.ItemExchangeScroll, "ItemExchangeScroll", "77", false));
        lockedScroll = test.Store(scroll);
        freeScroll = test.Store(scroll);
        ItemMetadata material = TestGameSession.Metadata(MaterialId, type: 1, slotMax: 1);
        lockedMaterial = test.Store(material);
        freeMaterial = test.Store(material);
        test.Register(TestGameSession.Metadata(RewardId, slotMax: 100));
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        test.TakeSent();
        lockedScroll = session.Item.Inventory.Get(lockedScroll.Uid)!;
        freeScroll = session.Item.Inventory.Get(freeScroll.Uid)!;
        lockedMaterial = session.Item.Inventory.Get(lockedMaterial.Uid)!;
        freeMaterial = session.Item.Inventory.Get(freeMaterial.Uid)!;
        return session;
    }

    private void Exchange(GameSession session, long scrollUid, int count) {
        new ItemExchangeScroll { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(scrollUid);
            writer.WriteLong(0);
            writer.WriteInt(count);
        }));
    }

    private static bool Held(GameSession session, BagItem item) => session.Item.Inventory.Get(item.Uid) != null;
    private static int Rewards(GameSession session) => session.Item.Inventory.Find(RewardId).Sum(item => item.Amount);

    // A locked scroll is not one of the two the exchange takes.
    [TestCase(true)]
    [TestCase(false)]
    public void LockedScroll_IsNotCounted(bool locked) {
        GameSession session = Start(out BagItem lockedScroll, out BagItem freeScroll, out _, out _);
        if (locked) {
            lockedScroll.IsLocked = true;
        } else {
            lockedScroll.UnlockTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        }

        Exchange(session, freeScroll.Uid, 2);
        Assert.That(Rewards(session), Is.Zero);
        Assert.That(Held(session, lockedScroll), Is.True);

        Exchange(session, freeScroll.Uid, 1);
        Assert.Multiple(() => {
            Assert.That(Rewards(session), Is.EqualTo(1));
            Assert.That(Held(session, lockedScroll), Is.True);
            Assert.That(Held(session, freeScroll), Is.False);
        });
    }

    // A locked material is left; the free one is taken.
    [Test]
    public void LockedMaterial_IsLeft() {
        GameSession session = Start(out BagItem lockedScroll, out _, out BagItem lockedMaterial, out BagItem freeMaterial);
        lockedMaterial.IsLocked = true;

        Exchange(session, lockedScroll.Uid, 2);
        Assert.That(Rewards(session), Is.Zero);
        Exchange(session, lockedScroll.Uid, 1);
        Assert.Multiple(() => {
            Assert.That(Rewards(session), Is.EqualTo(1));
            Assert.That(Held(session, lockedMaterial), Is.True);
            Assert.That(Held(session, freeMaterial), Is.False);
        });
    }

    // A spend on another thread while the materials leave: the fee was held at the check, so the exchange is done
    // and only what was left could go. Before, the fee was taken after the materials; that take threw on the short
    // balance, and the scroll and the material were gone with no reward.
    [Test]
    public void ASpendWhileTheMaterialsLeave_CannotSkipTheFee() {
        GameSession session = Start(out BagItem lockedScroll, out _, out _, out _, fee: 1000, meso: 1500);
        long spent = -1;
        test.OnSend = packet => {
            if (spent >= 0 || TestGameSession.Op(packet) != Maple2.Server.Core.Constants.SendOp.ItemInventory) {
                return;
            }
            long left = session.Currency.Meso;
            spent = session.Currency.TryTakeMeso(left, notify: false) ? left : 0;
        };

        Assert.DoesNotThrow(() => Exchange(session, lockedScroll.Uid, 1));

        Assert.Multiple(() => {
            Assert.That(spent, Is.EqualTo(500));
            Assert.That(session.Currency.Meso, Is.Zero);
            Assert.That(Rewards(session), Is.EqualTo(1));
        });
    }

    // A refusal after the fee is held gives it back: no reward can be made.
    [Test]
    public void RefusedAfterTheFeeIsHeld_TheFeeComesBack() {
        GameSession session = Start(out BagItem lockedScroll, out _, out BagItem lockedMaterial, out BagItem freeMaterial, fee: 1000, meso: 1500);
        lockedMaterial.IsLocked = true;
        freeMaterial.IsLocked = true;

        Exchange(session, lockedScroll.Uid, 1);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(1500));
            Assert.That(Rewards(session), Is.Zero);
            Assert.That(Held(session, lockedScroll), Is.True);
        });
    }
}
