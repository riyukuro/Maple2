using System.Linq;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client proposes, changes a reservation and enters a ceremony only with Wedding on (IsWeddingEnabled,
// weddingproxy.cpp:474): with it off those requests get no answer.
public class WeddingFeatureTests {
    // Wedding 3 (propose to a name not in the field) and 26 (enter the ceremony of a marriage with no hall): both are
    // answered with an error while the feature is on.
    [TestCase((byte) 3, true, ExpectedResult = 1)]
    [TestCase((byte) 3, false, ExpectedResult = 0)]
    [TestCase((byte) 26, true, ExpectedResult = 1)]
    [TestCase((byte) 26, false, ExpectedResult = 0)]
    public int RequestsNeedTheFeature(byte command, bool on) {
        var test = new TestGameSession();
        test.Start();
        if (!on) {
            test.SetFeaturesOff(WeddingHandler.Feature);
        }
        test.TakeSent();

        new WeddingHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            if (command == 3) {
                writer.WriteUnicodeString("Nobody");
            } else {
                writer.WriteLong(12345);
            }
        }));

        return test.TakeSentOps().Count(op => op == SendOp.Wedding);
    }
}
