using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using PartyManager = Maple2.Server.Game.Manager.PartyManager;

namespace Maple2.Server.Tests.Game.Manager;

// A party recall calls the members the client counts before it offers one (UIPartyDialog::FUN_141a77eb0): online, in a
// field it knows that is not a tutorial, less one for the caller. Before, any second member counted, so a scroll (or
// the merets) went on an offer that reached no one. The map ids are made up.
public class PartyRecallTests {
    private const int TutorialMap = 900, UnknownMap = 901;

    private static PartyMember Member(long characterId, short channel, int mapId) {
        var info = new PlayerInfo(new CharacterInfo(1, characterId, $"m{characterId}", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = channel,
            MapId = mapId,
        };
        return new PartyMember { PartyId = 1, Info = info };
    }

    private static bool IsCountedField(int mapId) => mapId != TutorialMap && mapId != UnknownMap;

    [Test]
    public void CountsOnlineMembersInKnownFieldsOutsideTutorials_LessTheCaller() {
        PartyMember caller = Member(10, 1, 100);
        PartyMember[] others = [
            Member(11, 2, 200),
            Member(12, -1, 200), // offline
            Member(13, 1, TutorialMap),
            Member(14, 1, UnknownMap),
        ];

        Assert.Multiple(() => {
            Assert.That(PartyManager.CountRecallable([caller, ..others], IsCountedField), Is.EqualTo(1));
            Assert.That(PartyManager.CountRecallable([caller, ..others[1..]], IsCountedField), Is.Zero, "no one to call");
        });
    }

    // The field's capacity (0: none) holds the players there and the called members.
    [TestCase(0, 50, 3, ExpectedResult = true)]
    [TestCase(10, 7, 3, ExpectedResult = true)]
    [TestCase(10, 8, 3, ExpectedResult = false)]
    public bool CalledMembersFitTheFieldsCapacity(int capacity, int inField, int recallable) =>
        PartyManager.RecallFits(capacity, inField, recallable);
}
