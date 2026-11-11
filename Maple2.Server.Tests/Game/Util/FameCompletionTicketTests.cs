using GameLua = Maple2.Server.Game.LuaFunctions.Lua;

namespace Maple2.Server.Tests.Game.Util;

// Quest 33: the number of completion tickets an alliance quest costs, the Lua calcFameCompletionTicketCount the
// client calls with the quest's alliance and rank (UIAllianceQuestDialog::FUN_140bf8c30). The alliance is not used.
public class FameCompletionTicketTests {
    [TestCase(0, 90)] // S
    [TestCase(1, 60)] // A
    [TestCase(2, 50)] // B
    [TestCase(3, 20)] // C
    [TestCase(4, 10)] // D
    [TestCase(5, 0)] // none: no completion with tickets
    [TestCase(-1, 0)]
    [TestCase(6, 0)]
    public void CalcFameCompletionTicketCount(int rank, int expected) {
        Assert.Multiple(() => {
            Assert.That(GameLua.CalcFameCompletionTicketCount(1, rank), Is.EqualTo(expected));
            Assert.That(GameLua.CalcFameCompletionTicketCount(9, rank), Is.EqualTo(expected));
        });
    }
}
