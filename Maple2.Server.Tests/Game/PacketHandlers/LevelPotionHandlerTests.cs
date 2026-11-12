using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The level potion (item function LevelPotion): KMS2 takes the potion out of the bag, then sends the level up.
public class LevelPotionHandlerTests {
    [Test]
    public void ThePotionLeavesTheBagBeforeTheLevelUp() {
        var test = new TestGameSession();
        Item potion = test.Store(TestGameSession.Metadata(20301234,
            function: new ItemMetadataFunction(ItemFunction.LevelPotion, "LevelPotion", "<v targetLevel=\"50\"/>", false), type: 2));
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(potion.Uid)));

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops.Take(2), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.LevelUp }));
        Assert.That(ops.Count(op => op == SendOp.ItemInventory), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Get(potion.Uid), Is.Null);
        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(50));
        Assert.That(test.World.PlayerUpdates.Select(update => update.Level), Is.EqualTo(new[] { 50 }));
        Assert.That(test.Errors, Is.Empty);
    }

    // A potion without a target level is kept.
    [Test]
    public void APotionWithoutATargetLevelIsKept() {
        var test = new TestGameSession();
        Item potion = test.Store(TestGameSession.Metadata(20301234,
            function: new ItemMetadataFunction(ItemFunction.LevelPotion, "LevelPotion", "<v/>", false), type: 2));
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(potion.Uid)));

        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.LevelUp));
        Assert.That(session.Item.Inventory.Get(potion.Uid)?.Amount, Is.EqualTo(1));
    }
}
