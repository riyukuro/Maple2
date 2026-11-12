using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The pet extraction scroll (item function PetExtraction): the client's dialog sends the scroll's use with the
// pet's uid as text. The pet is kept and a pet skin badge of its pet id is added; one scroll is used.
public class PetExtractionTests {
    private const int ScrollId = 20302314;
    private const int PetId = 60000001;
    private const int OtherPetId = 60000002;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ItemUseHandler handler = null!;
    private Item scroll = null!;
    private Item pet = null!;
    private Item closedPet = null!;
    private Item plain = null!;

    private static ItemMetadata PetItem(int petId) {
        ItemMetadata metadata = TestGameSession.Metadata(petId, type: 11, slotMax: 1);
        return metadata with { Property = metadata.Property with { PetId = petId } };
    }

    private void Login(int badges = 0) {
        test = new TestGameSession();
        test.RegisterPet(PetId, extraction: true);
        test.RegisterPet(OtherPetId);
        test.Register(TestGameSession.Metadata(ItemUseHandler.PetSkinBadgeItemId, type: 7, slotMax: 1));
        scroll = test.Store(TestGameSession.Metadata(ScrollId,
            function: new ItemMetadataFunction(ItemFunction.PetExtraction, "PetExtraction", "", false), type: 2), amount: 2);
        pet = test.Store(PetItem(PetId));
        closedPet = test.Store(PetItem(OtherPetId));
        plain = test.Store(TestGameSession.Metadata(20000001, type: 2));
        for (int i = 0; i < badges; i++) {
            test.Store(TestGameSession.Metadata(70100001, type: 7, slotMax: 1));
        }
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = new BanWordStorage(words) };
        test.Start();
        session = test.Session;
        test.TakeSent();
    }

    private void Use(string text) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(scroll.Uid);
        writer.WriteUnicodeString(text);
    }));

    private int Scrolls() => session.Item.Inventory.Get(scroll.Uid)?.Amount ?? 0;
    private Item[] Badges() => session.Item.Inventory.Filter(item => item.Id == ItemUseHandler.PetSkinBadgeItemId).ToArray();
    private string[] Answers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PetSkinBadge).Select(Convert.ToHexString).ToArray();
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // Refused as invalid: no number, an unknown uid, an item that is no pet, a pet whose pet allows no extraction.
    // Nothing is used or added.
    [TestCase("text")]
    [TestCase("")]
    [TestCase("unknown")]
    [TestCase("plain")]
    [TestCase("closed")]
    public void Refused_Invalid(string target) {
        Login();
        Use(target switch {
            "unknown" => "999999",
            "plain" => $"{plain.Uid}",
            "closed" => $"{closedPet.Uid}",
            _ => target,
        });

        Assert.That(Answers(), Is.EqualTo(new[] { Hex(PetExtractionPacket.Error(PetExtractionError.s_petextraction_invalid)) }));
        Assert.That(Scrolls(), Is.EqualTo(2));
        Assert.That(Badges(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(closedPet.Uid), Is.Not.Null);
    }

    // A full badge tab: the client's inventory full code, nothing used.
    [Test]
    public void Refused_InventoryFull() {
        Login(badges: 48);
        Use($"{pet.Uid}");

        Assert.That(Answers(), Is.EqualTo(new[] { Hex(PetExtractionPacket.Error(PetExtractionError.s_petextraction_inventoryfull)) }));
        Assert.That(Scrolls(), Is.EqualTo(2));
        Assert.That(Badges(), Is.Empty);
    }

    // The badge of the pet's id is added and saved, the pet is kept, one scroll is used, and the answer names both.
    [Test]
    public void Extracted() {
        Login();
        Use($"{pet.Uid}");

        Item[] badges = Badges();
        Assert.That(badges, Has.Length.EqualTo(1));
        Assert.That(badges[0].Badge, Is.Not.Null);
        Assert.That(badges[0].Badge!.Type, Is.EqualTo(BadgeType.PetSkin));
        Assert.That(badges[0].Badge!.PetSkinId, Is.EqualTo(PetId));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(PetExtractionPacket.Extracted(pet.Uid, badges[0].Uid, PetId)) }));
        Assert.That(Scrolls(), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Get(pet.Uid), Is.Not.Null);

        using GameStorage.Request db = test.Storage.Context();
        Item? stored = db.GetItem(badges[0].Uid);
        Assert.That(stored?.Badge?.PetSkinId, Is.EqualTo(PetId));
    }

    // The answer's layout (uipetextractionscrolldialog.cpp:229): op 0x108, the result byte, the pet's and the
    // badge's uid and the pet id; an error is the result byte alone.
    [Test]
    public void Packets() {
        Assert.That(Hex(PetExtractionPacket.Extracted(0x1122, 0x3344, PetId)),
            Is.EqualTo("080100" + "2211000000000000" + "4433000000000000" + "01879303"));
        Assert.That(Hex(PetExtractionPacket.Error(PetExtractionError.s_petextraction_inventoryfull)), Is.EqualTo("080102"));
    }
}
