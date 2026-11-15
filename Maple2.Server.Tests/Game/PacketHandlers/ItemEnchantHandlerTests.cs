using System;
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

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestItemEnchant (the client's UIItemEnchantDialog senders): the mode dispatch, the GM gate of modes 13 to 19
// (a non-GM is dropped with no reply), the type gate of the Ophelia and Peachy buttons and the stage error.
public class ItemEnchantHandlerTests {
    private TestGameSession test = null!;
    private GameSession session = null!;
    private ItemEnchantHandler handler = null!;
    private Item gear = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        session = test.Start();
        handler = new ItemEnchantHandler();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() => session?.Dispose();

    private void Send(byte mode, Action<IByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body?.Invoke(writer);
        }));
    }

    private void Stage(EnchantType type) => Send(1, writer => {
        writer.WriteByte((byte) type);
        writer.WriteLong(gear.Uid);
    });

    // The bag's own item: Store hands back the row it saved, the session works on its loaded copy.
    private Item Held() => session.Item.GetGear(gear.Uid)!;

    private void MakeGm() => session.Player.Value.Account.AdminPermissions = AdminPermissions.GameMaster;

    [Test]
    public void OpenDialog_DropsTheStagedItem() {
        Stage(EnchantType.Ophelia);
        Assert.That(session.ItemEnchant.Type, Is.EqualTo(EnchantType.Ophelia));

        Send(0);

        Assert.That(session.ItemEnchant.Type, Is.EqualTo(EnchantType.None));
    }

    [Test]
    public void StageItem_UnknownItem_AnswersTheInvalidItemError() {
        Send(1, writer => {
            writer.WriteByte((byte) EnchantType.Ophelia);
            writer.WriteLong(long.MaxValue);
        });

        Assert.That(test.TakeSent().Select(Convert.ToHexString).ToArray(),
            Is.EqualTo(new[] { Convert.ToHexString(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_invalid_item).ToArray()) }));
        Assert.That(session.ItemEnchant.Type, Is.EqualTo(EnchantType.None));
    }

    [Test]
    public void StageItem_OtherTypes_AreNotStaged() {
        Send(1, writer => {
            writer.WriteByte((byte) EnchantType.None);
            writer.WriteLong(gear.Uid);
        });

        Assert.That(session.ItemEnchant.Type, Is.EqualTo(EnchantType.None));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The Ophelia button (4) only counts while an Ophelia enchant is staged, the Peachy button (6) while a Peachy one.
    [Test]
    public void Ophelia_WithNothingStaged_IsIgnored() {
        Send(4, writer => {
            writer.WriteLong(gear.Uid);
            writer.WriteLong(0);
        });

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held().Enchant?.Enchants ?? 0, Is.EqualTo(0));
    }

    [Test]
    public void Peachy_WithAnOpheliaStaged_IsIgnored() {
        Stage(EnchantType.Ophelia);
        test.TakeSent();

        Send(6, writer => writer.WriteLong(gear.Uid));

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.ItemEnchant.Type, Is.EqualTo(EnchantType.Ophelia));
    }

    [Test]
    public void Ophelia_WithAPeachyStaged_IsIgnored() {
        Stage(EnchantType.Peachy);
        test.TakeSent();

        Send(4, writer => {
            writer.WriteLong(gear.Uid);
            writer.WriteLong(0);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void UpdateCharges_WithNothingStaged_IsIgnored() {
        Send(3, writer => writer.WriteInt(1));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void UpdateFodder_WithNothingStaged_IsIgnored() {
        Send(2, writer => {
            writer.WriteLong(gear.Uid);
            writer.WriteBool(true);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // Refund (9): the client's refund box never opens, so the mode answers nothing.
    [Test]
    public void Refund_AnswersNothing() {
        Send(9, writer => writer.WriteLong(gear.Uid));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // 13 to 19 come only from GM dialogs: a non-GM sender is dropped with no reply and no state change.
    [TestCase((byte) 13)]
    [TestCase((byte) 14)]
    [TestCase((byte) 15)]
    [TestCase((byte) 16)]
    [TestCase((byte) 17)]
    [TestCase((byte) 18)]
    [TestCase((byte) 19)]
    public void GmModes_FromANonGm_AreDropped(byte mode) {
        Stage(EnchantType.Ophelia);
        test.TakeSent();

        Send(mode, writer => {
            writer.WriteLong(gear.Uid);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteInt(0);
        });

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held().Enchant?.Enchants ?? 0, Is.EqualTo(0));
        Assert.That(Held().Enchant?.Charges ?? 0, Is.EqualTo(0));
    }

    // The simulation dialog modes have no answer in the client (OnItemEnchant reads none under 13 or 14).
    [TestCase((byte) 13)]
    [TestCase((byte) 14)]
    [TestCase((byte) 19)]
    public void SimulationModes_FromAGm_AnswerNothing(byte mode) {
        MakeGm();

        Send(mode, writer => {
            writer.WriteInt(1);
            writer.WriteInt(2);
            writer.WriteShort(3);
            writer.WriteInt(4);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void DebugFillFailStack_FromAGm_FillsTheItemsCharges() {
        MakeGm();
        Stage(EnchantType.Ophelia);
        test.TakeSent();

        Send(17, writer => writer.WriteLong(gear.Uid));

        Assert.That(Held().Enchant?.Charges ?? 0, Is.GreaterThan(0));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.ItemInventory));
    }

    [Test]
    public void DebugUseAllFailStack_FromAGm_EmptiesTheCharges() {
        MakeGm();
        Stage(EnchantType.Ophelia);
        Send(17, writer => writer.WriteLong(gear.Uid));
        test.TakeSent();

        Send(18, writer => writer.WriteLong(gear.Uid));

        Assert.That(Held().Enchant?.Charges ?? 0, Is.EqualTo(0));
    }

    [Test]
    public void DebugForceEnchant_FromAGm_EnchantsTheStagedItem() {
        MakeGm();
        Stage(EnchantType.Ophelia);
        test.TakeSent();

        Send(15);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(Held().Enchant?.Enchants ?? 0, Is.EqualTo(1));
    }

    [Test]
    public void DebugForceEnchant_WithNothingStaged_DoesNothing() {
        MakeGm();

        Send(15);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held().Enchant?.Enchants ?? 0, Is.EqualTo(0));
    }

    [Test]
    public void DebugFillIngredient_WithNothingStaged_DoesNothing() {
        MakeGm();

        Send(16, writer => {
            writer.WriteLong(gear.Uid);
            writer.WriteInt(10);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }
}
