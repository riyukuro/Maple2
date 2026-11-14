using System.Numerics;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Moves that name a spot on the field they go to: the way back from a home to the return field's saved spot, and a
// move to a player on another channel.
public class ReturnSpotMoveTests {
    private const int HomeMap = 62000000;
    private const int ReturnMap = 2000062;

    private static GameSession InHome(TestGameSession test, Vector3 spot) {
        GameSession session = test.Start();
        test.AddMap(TestGameSession.Map(ReturnMap) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        });
        test.EnterMap(TestGameSession.Map(2000023));
        Character character = session.Player.Value.Character;
        character.MapId = HomeMap;
        character.ReturnMaps.Push(ReturnMap);
        character.ReturnPosition = spot;
        test.TakeSent();
        return session;
    }

    // A refused move back (no server) leaves the player in the home, so the spot stays for the next way out.
    [Test]
    public void ReturnField_Refused_KeepsTheSpot() {
        var test = new TestGameSession();
        GameSession session = InHome(test, new Vector3(100, 200, 300));
        test.World.MoveError = new RpcException(new Status(StatusCode.Unavailable, "refused"));

        session.ReturnField();

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.That(session.Player.Value.Character.ReturnPosition, Is.EqualTo(new Vector3(100, 200, 300)));
    }

    // A move made carries the spot in the move and uses it up.
    [Test]
    public void ReturnField_Made_CarriesAndUsesTheSpot() {
        var test = new TestGameSession();
        GameSession session = InHome(test, new Vector3(100, 200, 300));

        session.ReturnField();

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(test.World.Moves[0].MapId, Is.EqualTo(ReturnMap));
            Assert.That(new Vector3(test.World.Moves[0].PositionX, test.World.Moves[0].PositionY, test.World.Moves[0].PositionZ),
                Is.EqualTo(new Vector3(100, 200, 300)));
            Assert.That(session.Player.Value.Character.ReturnPosition, Is.EqualTo(Vector3.Zero));
        });
    }

    private static PlayerInfo Target(short channel, int mapId) {
        return new PlayerInfo(new CharacterInfo(20000000002, 20000000003, "x", "", "", Gender.Female, Job.Newbie, 10), "", new AchievementInfo(), []) {
            Channel = channel,
            MapId = mapId,
        };
    }

    // The World's record of the target can still name the map the target just left: the move goes to the map the
    // target's channel read with the position. A channel that names no map leaves the record's.
    [TestCase(2000023, 2000023)]
    [TestCase(0, ReturnMap)]
    public void GoToPlayer_OtherChannel_GoesToTheMapOfThePosition(int warpMap, int expected) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000025));
        test.World.PlayerWarpAnswer = new Maple2.PlayerWarpResponse { RoomId = 5, MapId = warpMap, X = 1, Y = 2, Z = 3 };
        short other = (short) (session.Player.Value.Character.Channel + 1);

        Assert.That(GotoCommand.GoToPlayer(session, Target(other, ReturnMap)), Is.True);

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(test.World.Moves[0].MapId, Is.EqualTo(expected));
            Assert.That(test.World.Moves[0].PositionX, Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetMap_ChannelsMapFirst() {
        Assert.That(PlayerWarpUtil.TargetMap(2000023, ReturnMap), Is.EqualTo(2000023));
        Assert.That(PlayerWarpUtil.TargetMap(0, ReturnMap), Is.EqualTo(ReturnMap));
    }
}
