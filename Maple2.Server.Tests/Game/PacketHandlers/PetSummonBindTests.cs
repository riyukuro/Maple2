using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The summon binds a pet as the client's summon asks (s_msg_transfer_bind_pet_summon): any pet whose transfer rule
// has the bind bit (CItemTransfer::HasBindingRule) and no binding yet.
public class PetSummonBindTests {
    private const int PetId = 60000001;

    [TestCase(TransferType.BindPet, true)]
    [TestCase(TransferType.BindOnLoot, true)]
    [TestCase(TransferType.Untradeable, false)]
    [TestCase(TransferType.Tradable, false)]
    public void Summon_BindsAPetWithABindingRule(TransferType transferType, bool binds) {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        Item pet = test.Store(metadata with { Limit = metadata.Limit with { TransferType = transferType } });
        GameSession session = test.Start();
        pet = session.Item.Inventory.Get(pet.Uid)!;
        test.TakeSent();

        PetHandler.BindOnSummon(session, pet);

        Assert.That(pet.Transfer?.Binding?.CharacterId, binds ? Is.EqualTo(session.CharacterId) : Is.Null);
        Assert.That(test.TakeSentOps().Count(op => op == SendOp.BindItem), Is.EqualTo(binds ? 1 : 0));

        // A bound pet does not bind again.
        PetHandler.BindOnSummon(session, pet);
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.BindItem));
    }
}
