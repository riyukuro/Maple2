using System.Collections.Generic;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// enchant_result (ItemEnchantManager.Enchant, the end of a try), with the KR quest rows' conditions as the ingest
// reads them (ConvertCodes). The code is the try's result (1 success, 2 fail).
public class EnchantConditionTests {
    // KR 70600027: <condition type="enchant_result" code="1" value="1" />, and 70600001 (no code, value 1): an
    // Ophelia success counts for both; a row of the fail result does not count it.
    [Test]
    public void EnchantResult_70600027_ASuccess() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        GameSession session = test.Start();
        Quest Row(int id, string[] code) => test.StartQuest(TestGameSession.QuestData(id) with {
            Conditions = [new ConditionMetadata(ConditionType.enchant_result, 1, code.ConvertCodes(), new string[0].ConvertCodes())],
        });
        Quest success = Row(70600027, ["1"]);
        Quest any = Row(70600001, []);
        Quest fail = Row(91000002, ["2"]);

        session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid);
        Assert.That(session.ItemEnchant.Enchant(gear.Uid, force: true), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That((success.Conditions[0].Counter, any.Conditions[0].Counter, fail.Conditions[0].Counter), Is.EqualTo((1, 1, 0)));
    }
}
