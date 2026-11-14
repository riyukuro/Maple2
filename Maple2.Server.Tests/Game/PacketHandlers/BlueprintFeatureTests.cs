using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestMoveField 5 (blueprint designer) and 6 (model home) are sent only with Blueprint on (mypc.cpp:3116/4966):
// with it off the player is not moved.
public class BlueprintFeatureTests {
    [TestCase((byte) 5, false, ExpectedResult = 0)]
    [TestCase((byte) 6, false, ExpectedResult = 0)]
    [TestCase((byte) 6, true, ExpectedResult = 1)]
    public int MovesNeedBlueprint(byte command, bool on) {
        var test = new TestGameSession();
        test.Start();
        test.Session.Housing.InitNewHome("home", null);
        if (!on) {
            test.SetFeaturesOff(MoveFieldHandler.BlueprintFeature);
        }

        new MoveFieldHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(command)));

        return test.World.Moves.Count;
    }
}
