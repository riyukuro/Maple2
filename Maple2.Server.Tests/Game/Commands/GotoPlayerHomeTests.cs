using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Commands;

// GM "goto player X": X's home is the field the GM goes into when X stands in one. Before, the home map was asked
// for with no owner, which is the GM's own home.
public class GotoPlayerHomeTests {
    private const int HomeMap = 62000000;
    private const long TargetAccount = 20000000002;

    private static PlayerInfo Target(short channel, int mapId) {
        return new PlayerInfo(new CharacterInfo(TargetAccount, 20000000003, "x", "", "", Gender.Female, Job.Newbie, 10), "", new AchievementInfo(), []) {
            Channel = channel,
            MapId = mapId,
        };
    }

    [Test]
    public void GotoPlayer_InAHomeOnAnotherChannel_MovesIntoThatHome() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        test.World.PlayerWarpAnswer = new Maple2.PlayerWarpResponse { RoomId = 10000010, OwnerId = TargetAccount, X = 1, Y = 2, Z = 3 };
        short other = (short) (session.Player.Value.Character.Channel + 1);

        Assert.That(GotoCommand.GoToPlayer(session, Target(other, HomeMap)), Is.True);

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(test.World.Moves[0].MapId, Is.EqualTo(HomeMap));
            Assert.That(test.World.Moves[0].OwnerId, Is.EqualTo(TargetAccount));
            Assert.That(test.World.Moves[0].RoomId, Is.EqualTo(10000010));
            Assert.That(test.World.Moves[0].Channel, Is.EqualTo(other));
        });
    }

    [Test]
    public void GotoPlayer_OutsideAHome_SendsNoOwner() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        test.World.PlayerWarpAnswer = new Maple2.PlayerWarpResponse { RoomId = 5, X = 1, Y = 2, Z = 3 };
        short other = (short) (session.Player.Value.Character.Channel + 1);

        Assert.That(GotoCommand.GoToPlayer(session, Target(other, 2000062)), Is.True);

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.That(test.World.Moves[0].HasOwnerId, Is.False);
    }
}
