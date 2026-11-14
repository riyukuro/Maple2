using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Attendance 2 (gift window opened) and 3 (merets pay box opened) carry no body. The client sends each without waiting
// for an answer and reads only Attendance 5, 6, 7 and 9 back, so the server sends nothing and changes nothing.
public class AttendanceWindowModeTests {
    private const int EventId = 7102;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(20000001));
        test.AddEvent(EventId, GameEventType.AttendGift,
            new AttendGift([new RewardItem(20000001, 1, 1)], "gift", "title", "content", string.Empty, 0, null));
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private static void Send(GameSession session, byte mode) {
        new AttendanceHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(mode)));
    }

    [TestCase((byte) 2)]
    [TestCase((byte) 3)]
    public void TheModeIsAnsweredWithNothing(byte mode) {
        GameSession session = Start();
        string? claimed = session.GameEvent.Peek(GameEventUserValueType.AttendanceRewardsClaimed, EventId);
        long meso = session.Currency.Meso;

        Send(session, mode);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new List<byte[]>()));
            Assert.That(session.GameEvent.Peek(GameEventUserValueType.AttendanceRewardsClaimed, EventId), Is.EqualTo(claimed));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
