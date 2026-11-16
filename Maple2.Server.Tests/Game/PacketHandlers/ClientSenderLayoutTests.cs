using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Each handler reads exactly the bytes the client's sender writes: the packet is built field by field as the named
// client function writes it and nothing is left over once the handler has read it. A handler that reads too little
// or too much leaves its next field off by that much. The ids are made up.
public class ClientSenderLayoutTests {
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    private void Consumes(PacketHandler<GameSession> handler, Action<ByteWriter> client) {
        ByteReader packet = TestGameSession.Packet(client);
        handler.Handle(test.Session, packet);
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // As Consumes, for a handler whose action needs the world or tables the test session lacks: it fails after it has
    // read its fields, which is all this checks.
    private void ReadsAll(PacketHandler<GameSession> handler, Action<ByteWriter> client) {
        ByteReader packet = TestGameSession.Packet(client);
        try {
            handler.Handle(test.Session, packet);
        } catch (Exception) {
            // The side effect, not the read.
        }
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // CCashMarketMan::SendCashMode0, Mode1, Mode5 (cashinventorybag.cpp:330-372): the mode, then page for 5.
    [Test]
    public void Cash_Modes0_1_5() {
        Consumes(new CashHandler(), w => w.WriteByte(0));
        Consumes(new CashHandler(), w => w.WriteByte(1));
        Consumes(new CashHandler(), w => { w.WriteByte(5); w.WriteInt(1); });
    }

    // CStatPointManager::RequestAdd and RequestReset (statpointmanager.cpp:123-151): 2 stat int, 3.
    [Test]
    public void AttributePoint_Modes2_3() {
        ReadsAll(new AttributePointHandler(), w => { w.WriteByte(2); w.WriteByte(1); w.WriteInt(1); });
        ReadsAll(new AttributePointHandler(), w => w.WriteByte(3));
    }

    // UIUnlimitedEnchantDialog stage and enchant (uiunlimitedenchantdialog.cpp:368-402): mode, item uid.
    [Test]
    public void LimitBreak_Modes0_1() {
        Consumes(new LimitBreakHandler(), w => { w.WriteByte(0); w.WriteLong(1); });
        Consumes(new LimitBreakHandler(), w => { w.WriteByte(1); w.WriteLong(1); });
    }

    // Unk_Inventory::RequestEquipItem / RequestUnequipItem and EquipSlot::vfunc_18 (inventory.cpp:1258-1300,
    // equipslot.cpp:168-180): 0 uid slot kind, 1 uid, 2 uid slot.
    [Test]
    public void ItemEquip_Modes0_1_2() {
        Consumes(new ItemEquipHandler(), w => { w.WriteByte(0); w.WriteLong(1); w.WriteByte(1); w.WriteByte(0); });
        Consumes(new ItemEquipHandler(), w => { w.WriteByte(1); w.WriteLong(1); });
        Consumes(new ItemEquipHandler(), w => { w.WriteByte(2); w.WriteLong(1); w.WriteByte(1); });
    }

    // CPCInfo party menu (uipartydialog.cpp:3151): the character id as 8 bytes.
    [Test]
    public void CharacterInfo_TheCharacterId() {
        ReadsAll(new CharacterInfoHandler(), w => w.WriteLong(1));
    }

    // MyPC meso pickup (mypc.cpp:4813-4830): a count byte, then each drop's object id.
    [Test]
    public void MesoPickup_CountAndObjectIds() {
        Consumes(new MesoPickupHandler(), w => { w.WriteByte(2); w.WriteInt(11); w.WriteInt(12); });
    }

    // UIMessageBox::HandleChangeCharNameButton (uimessagebox.cpp:2109): the name, then the item uid.
    [Test]
    public void CheckCharName_NameAndUid() {
        Consumes(new CheckCharacterNameHandler { GameStorage = test.Storage, BanWordStorage = TestGameSession.BanWords() },
            w => { w.WriteUnicodeString("name"); w.WriteLong(1); });
    }

    // The gem (badge) window's equip and unequip (gemslot_2.cpp:466-500, payloads 0x1401fd190, 0x1401fd250): 0 uid slot,
    // 1 slot. The hide mode (3: slot and ten flags, uigemsettingdialog.cpp:148-157) reads its flags only for a worn
    // transparency badge, which the test session has none of.
    [Test]
    public void BadgeEquip_Modes0_1() {
        ReadsAll(new BadgeEquipHandler(), w => { w.WriteByte(0); w.WriteLong(1); w.WriteByte(1); });
        ReadsAll(new BadgeEquipHandler(), w => { w.WriteByte(1); w.WriteByte(1); });
    }

    // The red envelope chat link (uimessenger.cpp:4745) (the staff's remove, mode 8 and the board uid, is read for a GM only).
    [Test]
    public void ItemBillboard_Mode1() {
        ReadsAll(new ItemBillboardHandler(), w => { w.WriteByte(1); w.WriteInt(1); });
    }

    // UIBirthdayCardDialog::SendBirthdayCard (uibirthdaycarddialog.cpp:98): mode 0.
    [Test]
    public void BirthdayCard_Mode0() {
        ReadsAll(new BirthdayCardHandler(), w => w.WriteByte(0));
    }

    // CSceneStateGame::OnServerEnter (scenestategame.cpp:2150): the list version, 8 bytes.
    [Test]
    public void DynamicBanword_TheVersion() {
        ReadsAll(new DynamicBanwordHandler(), w => w.WriteLong(0));
    }

    // CNpc::SendInteractNpc (npcactorandvibrate.cpp:1441): the object id.
    [Test]
    public void InteractNpc_TheObjectId() {
        ReadsAll(new InteractNpcHandler(), w => w.WriteInt(1));
    }

    // UICharacterAbilityDialog learn, reset and the GM learn-all (uicharacterabilitydialog.cpp:422, 504,
    // usercommandcli.cpp:1961): 2 and the ability id, 1, 3.
    [Test]
    public void CharacterAbility_Modes1_2_3() {
        ReadsAll(new CharacterAbilityHandler(), w => { w.WriteByte(2); w.WriteInt(1); });
        ReadsAll(new CharacterAbilityHandler(), w => w.WriteByte(1));
        ReadsAll(new CharacterAbilityHandler(), w => w.WriteByte(3));
    }

    // The party dialog's give up and the reward dialog's leave (uipartydialog.cpp:787, uidungeonrewarddialog.cpp:553):
    // the mode alone, 5 and 6.
    [Test]
    public void DungeonReward_Modes5_6() {
        ReadsAll(new DungeonRewardHandler(), w => w.WriteByte(5));
        ReadsAll(new DungeonRewardHandler(), w => w.WriteByte(6));
    }
}
