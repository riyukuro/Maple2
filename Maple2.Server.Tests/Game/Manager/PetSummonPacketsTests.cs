using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Manager;

// What the owner gets after a pet's field entry. On a summon KMS2 sends the pet's state, the collection entry
// when the summon made one, then the "pet summoned" notice (the client prints only the dismissal line itself).
public class PetSummonPacketsTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static string[] Packets((int, short)? collection, bool summoned) =>
        PetManager.LoadPackets(7, new ItemPet(), new PetConfig(), collection, summoned).Select(Hex).ToArray();

    private static string Notice => Hex(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_pet_summon_on));

    [Test]
    public void FirstSummon_SendsStateCollectionNotice() {
        Assert.That(Packets((60000001, 4), summoned: true), Is.EqualTo(new[] {
            Hex(PetPacket.Load(7, new ItemPet(), new PetConfig())),
            Hex(PetPacket.AddCollection(60000001, 4)),
            Notice,
        }));
    }

    [Test]
    public void LaterSummon_SendsStateNotice() {
        Assert.That(Packets(null, summoned: true), Is.EqualTo(new[] { Hex(PetPacket.Load(7, new ItemPet(), new PetConfig())), Notice }));
    }

    [Test]
    public void FieldChange_SendsStateOnly() {
        Assert.That(Packets(null, summoned: false), Is.EqualTo(new[] { Hex(PetPacket.Load(7, new ItemPet(), new PetConfig())) }));
    }

    // Notice mode 4, flags 5, one interface text with no arguments, duration 0.
    [Test]
    public void Notice_Shape() {
        byte[] notice = NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_pet_summon_on).ToArray();
        Assert.That(Convert.ToHexString(notice[2..10]), Is.EqualTo("0405000101000000"));
        Assert.That(notice.Length, Is.EqualTo(2 + 16));
        Assert.That(Convert.ToHexString(notice[^4..]), Is.EqualTo("00000000"));
    }
}
