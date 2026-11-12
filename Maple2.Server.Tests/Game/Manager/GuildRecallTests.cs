using Maple2.Model.Enum;
using Maple2.Model.Game;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.Manager;

public class GuildRecallTests {
    private const int TutorialMap = 900;

    private static GuildMember Member(long characterId, short channel, int mapId) {
        var info = new PlayerInfo(new CharacterInfo(1, characterId, $"m{characterId}", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = channel,
            MapId = mapId,
        };
        return new GuildMember { Info = info };
    }

    private static bool IsTutorial(int mapId) => mapId == TutorialMap;

    // The client's count (CGuildInfo::FUN_1404d3400): online members in a field that is not a tutorial; the
    // caller is left out, so a guild with only the caller online calls no one and the scroll is kept.
    [Test]
    public void CountsOnlineMembersOutsideTutorialsButTheCaller() {
        GuildMember[] members = [
            Member(10, 1, 100), // the caller
            Member(11, 2, 200),
            Member(12, -1, 200), // offline
            Member(13, 1, TutorialMap),
        ];

        Assert.That(GuildManager.CountRecallable(members, 10, IsTutorial), Is.EqualTo(1));
        Assert.That(GuildManager.CountRecallable(members[..1], 10, IsTutorial), Is.EqualTo(0));
        Assert.That(GuildManager.CountRecallable([members[0], members[2], members[3]], 10, IsTutorial), Is.EqualTo(0));
    }
}
