using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// QuestManager.MaskShows: an npc without a mask row is shown; a masked npc is shown by the player's quest states
// (QuestMaskRules). Made-up map, npc and quest ids.
public class QuestMaskShowsTests {
    private const int Map = 2000001, MaskedNpc = 11009001, OtherNpc = 11009002, QuestId = 70000101;

    private TestGameSession test = null!;

    private GameSession Start(bool enable) {
        test = new TestGameSession();
        // Range: the quest started (start mode 1 reached), no end quest mode reached (mode 3 is never passed while open).
        var mask = new QuestMaskNpcTable.Mask(enable, [new QuestMaskNpcTable.Range(QuestId, 1, QuestId, 3)]);
        test.SetTable(new QuestMaskNpcTable(new Dictionary<int, IReadOnlyDictionary<int, QuestMaskNpcTable.Mask>> {
            [Map] = new Dictionary<int, QuestMaskNpcTable.Mask> { [MaskedNpc] = mask },
        }));
        return test.Start();
    }

    [Test]
    public void NpcWithoutAMaskRowIsShown() {
        GameSession session = Start(true);
        Assert.Multiple(() => {
            Assert.That(session.Quest.MaskShows(Map, OtherNpc), Is.True);
            Assert.That(session.Quest.MaskShows(2000099, MaskedNpc), Is.True);
        });
    }

    // Without the quest the range is not reached: the npc follows the opposite of enableInMask.
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void MaskedNpc_WithoutTheQuest(bool enable, bool shown) {
        GameSession session = Start(enable);
        Assert.That(session.Quest.MaskShows(Map, MaskedNpc), Is.EqualTo(shown));
    }

    // With the quest started the range holds: the npc follows enableInMask.
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void MaskedNpc_WithTheQuestStarted(bool enable, bool shown) {
        GameSession session = Start(enable);
        test.StartQuest(TestGameSession.QuestData(QuestId));
        Assert.That(session.Quest.MaskShows(Map, MaskedNpc), Is.EqualTo(shown));
    }
}
