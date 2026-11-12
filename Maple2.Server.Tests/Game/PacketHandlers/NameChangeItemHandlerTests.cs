using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The name change item (item function ChangeCharName) through ItemUseHandler.Handle. KMS2 takes the item out of
// the bag, answers CharacterList 7 with the character and the new name, then sends the guild's and the clubs'
// member name (the world's player update).
public class NameChangeItemHandlerTests {
    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item voucher = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        voucher = test.Store(TestGameSession.Metadata(20300123,
            function: new ItemMetadataFunction(ItemFunction.ChangeCharName, "ChangeCharName", "", false), type: 2));
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        words.BanWordMetadata.Add(new BanWordMetadata(0, "badword", false));
        words.SaveChanges();
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = new BanWordStorage(words) };
    }

    private void Use(GameSession session, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(voucher.Uid);
            writer.WriteUnicodeString(name);
        }));
    }

    private string StoredName() {
        using var context = new Ms2Context(test.Options);
        return context.Character.Find(test.CharacterId)!.Name;
    }

    [Test]
    public void TheNameIsStoredAndAnsweredAfterTheItemLeavesTheBag() {
        GameSession session = test.Start();
        test.TakeSent();
        test.World.PlayerUpdates.Clear();

        Use(session, "Parry");

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.CharacterList }));
            Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Convert.ToHexString(CharacterListPacket.NameChanged(test.CharacterId, "Parry").ToArray())));
            Assert.That(session.Item.Inventory.Get(voucher.Uid), Is.Null);
            Assert.That(session.Player.Value.Character.Name, Is.EqualTo("Parry"));
            Assert.That(StoredName(), Is.EqualTo("Parry"));
            Assert.That(test.World.PlayerUpdates.Select(update => (update.CharacterId, update.HasName, update.Name)),
                Is.EqualTo(new[] { (test.CharacterId, true, "Parry") }));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Each refusal: the client's text in a message box; the item stays, the name stays, the world is not told.
    [TestCase("", NameChangeRules.Refusal.Invalid)]
    [TestCase("A", NameChangeRules.Refusal.Invalid)]
    [TestCase("Abcdefghijklm", NameChangeRules.Refusal.Invalid)]
    [TestCase("Par_ry", NameChangeRules.Refusal.Invalid)]
    [TestCase("xBadWordx", NameChangeRules.Refusal.Banned)]
    [TestCase("taKEN", NameChangeRules.Refusal.Taken)]
    public void ARefusedNameUsesNothingUp(string name, NameChangeRules.Refusal refusal) {
        using (GameStorage.Request db = test.Storage.Context()) {
            db.CreateCharacter(new Character {
                AccountId = 2000, Name = "Taken", Gender = Gender.Male, Job = Job.Newbie, Mastery = new Mastery(),
                MapId = 2000023, ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
            });
        }
        GameSession session = test.Start();
        string old = session.Player.Value.Character.Name;
        test.TakeSent();
        test.World.PlayerUpdates.Clear();

        Use(session, name);

        (StringCode code, string[] args) = NameChangeRules.Text(refusal, name);
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(NoticePacket.MessageBox(new InterfaceText(code, args)).ToArray()),
            }));
            Assert.That(session.Item.Inventory.Get(voucher.Uid)?.Amount, Is.EqualTo(1));
            Assert.That(session.Player.Value.Character.Name, Is.EqualTo(old));
            Assert.That(StoredName(), Is.EqualTo(old));
            Assert.That(test.World.PlayerUpdates, Is.Empty);
        });
    }

    // A create that stores the name after the rename is found by the create's own check at once: the name is
    // written with the rename, not at the logout save.
    [Test]
    public void TheNewNameIsTakenForACreateAtOnce() {
        GameSession session = test.Start();

        Use(session, "Parry");

        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCharacterId("parry"), Is.EqualTo(test.CharacterId));
    }
}
