using System;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// Pet mode 21 (CPetManager::FUN_140c8afa0, petmanager.cpp:1058): int owner object id, then the pet item as
// PC::ReadPetItem reads it (bool, i32 id, i64 uid, i32 rarity, the item), the same tail as the summon's.
public class PetItemUpdateTests {
    [Test]
    public void ItemUpdate_IsPetMode21WithTheOwnerAndThePetItem() {
        ItemMetadata metadata = TestGameSession.Metadata(60000001, type: 11, slotMax: 1);
        var pet = new Item(metadata with { Property = metadata.Property with { PetId = 60000001 } }, 3) { Uid = 0x0102030405060708 };
        Assert.That(pet.Pet, Is.Not.Null);

        byte[] packet = PetPacket.ItemUpdate(0x11223344, pet).ToArray();

        ByteWriter item = Packet.Of(SendOp.ResponsePet);
        item.WriteClass<Item>(pet);
        byte[] itemBytes = item.ToArray()[2..];
        byte[] head = [
            0x15, // mode
            0x44, 0x33, 0x22, 0x11, // owner object id
            0x01, // has a pet
            0x01, 0x87, 0x93, 0x03, // 60000001
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // uid
            0x03, 0x00, 0x00, 0x00, // rarity
        ];
        Assert.That(packet[..2], Is.EqualTo(new[] { (byte) SendOp.ResponsePet, (byte) ((int) SendOp.ResponsePet >> 8) }));
        Assert.That(packet[2..], Is.EqualTo(head.Concat(itemBytes).ToArray()));
    }
}
