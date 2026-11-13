using Maple2.Model.Enum;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// The completed vow counts wedding_complete for the couple and wedding_guest for anyone else in the room.
public class WeddingVowConditionTests {
    private const long Groom = 1001;
    private const long Bride = 1002;
    private const long Guest = 1003;

    [TestCase(Groom, ConditionType.wedding_complete)]
    [TestCase(Bride, ConditionType.wedding_complete)]
    [TestCase(Guest, ConditionType.wedding_guest)]
    public void VowCondition(long characterId, ConditionType type) {
        var ceremony = new WeddingCeremony(7, Groom, Bride, Groom);
        Assert.That(ceremony.VowCondition(characterId), Is.EqualTo(type));
    }
}
