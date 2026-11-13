using System;
using System.Linq;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// AdventureProtectManager: opcode 0x131 with the state, sent at the entry and after it only when it changes.
public class AdventureProtectManagerTests {
    [Test]
    public void Update_SendsTheStateWhenItChanges() {
        var test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureProtectRequireLevel = 30 });
        GameSession session = test.Start();
        session.Player.Value.Character.Level = 29;
        test.TakeSent();

        session.AdventureProtect.Update();
        Assert.That(States(test), Is.Empty, "nothing before the entry");

        session.AdventureProtect.Load();
        session.AdventureProtect.Update();
        Assert.That(States(test), Is.EqualTo(new[] { "06" }));
        Assert.That(session.AdventureProtect.Cleared, Is.False);

        session.Player.Value.Character.Level = 30;
        session.AdventureProtect.Update();
        Assert.That(States(test), Is.EqualTo(new[] { "07" }));
        Assert.That(session.AdventureProtect.Cleared, Is.True);
    }

    private static string[] States(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.AdventureProtect)
            .Select(packet => Convert.ToHexString(packet)[4..]).ToArray();
}
