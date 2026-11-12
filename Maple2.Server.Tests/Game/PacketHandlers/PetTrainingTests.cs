using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The pet training kits (item function PetTraining, 64000001-64000008): UIItemPetTrainingDialog takes a pet item of
// pet type 1 below the top level, and its confirm sends the plain item use with the pet's uid as text. The server
// uses one kit, gives the kit's offsetExp as a fusion gives exp and sends ResponsePet 13, which completes the dialog.
public class PetTrainingTests {
    private const int PetId = 60000001, KitId = 64000001, MaxLevel = 50;

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item kit = null!;
    private Item pet = null!;

    private GameSession Start(string parameters = "<v offsetExp=\"1500\" />", int petType = 1, int kits = 2) {
        test = new TestGameSession();
        test.RegisterPet(PetId, petType);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { PetMaxLevel = MaxLevel });
        // 1000 exp a level for every rarity.
        test.SetTable(new PetExpTable(Enumerable.Range(1, 50).ToDictionary(level => level, _ => Enumerable.Repeat(1000L, PetExpTable.Columns).ToArray())));
        ItemMetadata petItem = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        long petUid = test.Store(petItem with { Property = petItem.Property with { PetId = PetId } }).Uid;
        kit = test.Store(TestGameSession.Metadata(KitId,
            function: new ItemMetadataFunction(ItemFunction.PetTraining, "PetTraining", parameters, false), type: 10), amount: kits);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        pet = session.Item.Inventory.Get(petUid)!;
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session, string text) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(kit.Uid);
        writer.WriteUnicodeString(text);
    }));

    private int Kits(GameSession session) => session.Item.Inventory.Get(kit.Uid)?.Amount ?? 0;

    [Test]
    public void Training_GivesTheExpAndCompletes() {
        GameSession session = Start();
        pet.Pet!.Level = 3;
        pet.Pet.Exp = 700;

        Use(session, pet.Uid.ToString());

        // 700 + 1500 = 2200: two levels (1000 each), 200 left.
        Assert.That((pet.Pet.Level, pet.Pet.Exp), Is.EqualTo(((short) 5, 200L)));
        Assert.That(Kits(session), Is.EqualTo(1));
        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponsePet).ToList();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(PetPacket.Fusion(session.Player.ObjectId, pet).ToArray()),
            Convert.ToHexString(PetPacket.LevelUp(session.Player.ObjectId, pet).ToArray()),
            Convert.ToHexString(PetPacket.TrainingComplete().ToArray()),
        }));
        Assert.That(sent[^1], Is.EqualTo(new byte[] { (byte) SendOp.ResponsePet, (byte) ((int) SendOp.ResponsePet >> 8), 13 }));
    }

    // Exp below the next level: no level-up packet.
    [Test]
    public void Training_WithoutLevelUp() {
        GameSession session = Start("<v offsetExp=\"300\" />");

        Use(session, pet.Uid.ToString());

        Assert.That(pet.Pet!.Exp, Is.EqualTo(300));
        Assert.That(test.TakeSentOps().Count(op => op == SendOp.ResponsePet), Is.EqualTo(2));
    }

    // The preview's cap: reaching the top level sets the exp to 0.
    [Test]
    public void Training_StopsAtTheTopLevel() {
        GameSession session = Start("<v offsetExp=\"120000\" />");
        pet.Pet!.Level = MaxLevel - 1;

        Use(session, pet.Uid.ToString());

        Assert.That((pet.Pet.Level, pet.Pet.Exp), Is.EqualTo(((short) MaxLevel, 0L)));
    }

    // The dialog refuses a pet at the top level, a pet of another type and a pet without pet data; the server
    // refuses the same and a uid of no own pet or a bad text, and a kit without offsetExp: no kit is used.
    [TestCase("max")]
    [TestCase("type")]
    [TestCase("uid")]
    [TestCase("text")]
    [TestCase("kit")]
    public void Training_Refused(string why) {
        GameSession session = Start(why == "kit" ? "<v />" : "<v offsetExp=\"1500\" />", petType: why == "type" ? 2 : 1);
        if (why == "max") {
            pet.Pet!.Level = MaxLevel;
        }
        string text = why switch {
            "uid" => kit.Uid.ToString(),
            "text" => "x",
            _ => pet.Uid.ToString(),
        };
        short level = pet.Pet!.Level;

        Use(session, text);

        Assert.That(Kits(session), Is.EqualTo(2));
        Assert.That((pet.Pet.Level, pet.Pet.Exp), Is.EqualTo((level, 0L)));
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.ResponsePet));
    }

    [TestCase("<v offsetExp=\"1500\" />", 1500)]
    [TestCase("<v offsetExp=\" 120000 \" />", 120000)]
    [TestCase("<v />", 0)]
    [TestCase("<v offsetExp=\"-5\" />", 0)]
    public void TrainingExp(string parameters, long expected) {
        Assert.That(PetFusionRules.TrainingExp(XmlParseUtil.GetParameters(parameters)), Is.EqualTo(expected));
    }

    [TestCase(1, 49, true)]
    [TestCase(1, 50, false)]
    [TestCase(2, 10, false)]
    [TestCase(0, 10, false)]
    public void IsTrainingTarget(int type, int level, bool expected) {
        Assert.That(PetFusionRules.IsTrainingTarget(type, level, MaxLevel), Is.EqualTo(expected));
    }
}
