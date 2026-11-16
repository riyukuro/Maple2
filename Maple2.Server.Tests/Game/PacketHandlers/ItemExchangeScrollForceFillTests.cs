using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The exchange scroll dialog's two GM buttons (UIItemExchangeScrollDialog FUN_140f55b90 and FUN_140f55c50,
// uiitemexchangescrolldialog.cpp:885-915, GM level 50 or more) send ItemExchangeScroll 3 with ForceFillIngredient
// (FUN_140202580): the scroll uid, the target uid and a count, 0 ("max") or 10. A GM gets each ingredient of the
// scroll's row, count times or a full stack, and the dialog's recount (ItemExchange 3); anyone else gets nothing.
public class ItemExchangeScrollForceFillTests {
    private const int ScrollId = 20000001;
    private const int MaterialId = 30000001;

    private TestGameSession test = null!;
    private long scrollUid;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new ItemExchangeScrollTable(new Dictionary<int, ItemExchangeScrollMetadata> {
            [77] = new(new ItemComponent(ScrollId, -1, 1, ItemTag.None), new ItemComponent(30000002, 1, 1, ItemTag.None), 0, 0,
                [new ItemComponent(MaterialId, 1, 3, ItemTag.None)]),
        }));
        scrollUid = test.Store(TestGameSession.Metadata(ScrollId, type: 1, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.ItemExchangeScroll, "ItemExchangeScroll", "77", false))).Uid;
        test.Register(TestGameSession.Metadata(MaterialId, slotMax: 50));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
    }

    private void Fill(int count) {
        new ItemExchangeScroll { ItemMetadata = test.Session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(scrollUid);
            writer.WriteLong(0);
            writer.WriteInt(count);
        }));
    }

    private int Materials() => test.Session.Item.Inventory.Find(MaterialId).Sum(item => item.Amount);

    private string[] Sent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.ItemExchange).Select(Convert.ToHexString).ToArray();

    [TestCase(10, 10)]
    [TestCase(0, 50)]
    public void AGmFill_GivesTheIngredients(int count, int given) {
        test.Session.Player.AdminPermissions = AdminPermissions.GameMaster;
        Fill(count);
        Assert.Multiple(() => {
            Assert.That(Materials(), Is.EqualTo(given));
            Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(ItemExchangeScrollPacket.RefreshIngredients().ToArray()) }));
        });
    }

    [Test]
    public void AFill_NotFromAGm_GivesNothing() {
        test.Session.Player.AdminPermissions = AdminPermissions.None;
        Fill(10);
        Assert.Multiple(() => {
            Assert.That(Materials(), Is.Zero);
            Assert.That(Sent(), Is.Empty);
        });
    }
}
