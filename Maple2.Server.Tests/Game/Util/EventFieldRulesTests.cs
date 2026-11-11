using System.Runtime.CompilerServices;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class EventFieldRulesTests {
    [Test]
    public void Decide_MovesOnlyToAFieldWithARunningEvent() {
        int[] running = [2000100, 2000200];

        Assert.That(EventFieldRules.Decide(true, false, 2000001, 2000100, running), Is.EqualTo(EventFieldRules.Result.Move));
        Assert.That(EventFieldRules.Decide(true, false, 2000001, 2000300, running), Is.EqualTo(EventFieldRules.Result.NoEvent));
        Assert.That(EventFieldRules.Decide(true, false, 2000001, 2000300, []), Is.EqualTo(EventFieldRules.Result.NoEvent));
    }

    // The client never sends these, so they get no answer.
    [Test]
    public void Decide_IgnoresWhatTheClientRefusesItself() {
        int[] running = [2000100];

        Assert.That(EventFieldRules.Decide(false, false, 0, 2000100, running), Is.EqualTo(EventFieldRules.Result.Ignore));
        Assert.That(EventFieldRules.Decide(true, true, 2000001, 2000100, running), Is.EqualTo(EventFieldRules.Result.Ignore));
        Assert.That(EventFieldRules.Decide(true, false, 2000100, 2000100, running), Is.EqualTo(EventFieldRules.Result.Ignore));
        Assert.That(EventFieldRules.Decide(true, false, 2000001, 0, running), Is.EqualTo(EventFieldRules.Result.Ignore));
        Assert.That(EventFieldRules.Decide(true, false, 2000001, -5, running), Is.EqualTo(EventFieldRules.Result.Ignore));
    }

    // A plain (non-GM) session that is in no field: the request is read whole and nothing else is touched.
    [Test]
    public void Handler_ReadsTheFieldIdAndStopsWithoutAField() {
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        System.GC.SuppressFinalize(session); // never constructed, so its finalizer has nothing to release
        var writer = new ByteWriter();
        writer.WriteInt(2000100);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.DoesNotThrow(() => new MoveEventFieldHandler().Handle(session, reader));
        Assert.That(reader.Position, Is.EqualTo(4));
    }

    [Test]
    public void Refuse_IsOneByte() {
        ByteWriter packet = MoveEventFieldPacket.Refuse(MoveEventFieldPacket.NO_EVENT);

        Assert.That(packet.Length, Is.EqualTo(3));
        Assert.That(packet.Buffer[2], Is.EqualTo(0));
    }
}
