using System;
using System.Linq;
using System.Reflection;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A session torn down while its load ran (a manager's constructor threw in EnterServer): the player is there, the
// managers after the one that threw are not. Its save stores the player row and skips the managers never built.
public class SessionSaveUnbuiltManagersTests {
    [Test]
    public void Save_SkipsTheManagersNeverBuilt() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        // As a load whose ConfigManager threw: it and every manager built after it are missing.
        session.Config = null!;
        session.Housing = null!;
        session.GameEvent = null!;
        session.Achievement = null!;
        session.Quest = null!;
        session.Shop = null!;
        session.Item = null!;
        session.UgcMarket = null!;
        session.Survival = null!;
        session.Dungeon = null!;
        session.Player.Value.Character.Level = 7;

        MethodInfo save = typeof(GameSession).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(TimeSpan)])!;
        Assert.That(save.Invoke(session, [TimeSpan.FromSeconds(1)]), Is.True);

        Assert.That(test.Errors, Is.Empty);
        using var db = test.Storage.Context();
        Assert.That(db.GetCharacter(test.CharacterId, test.AccountId)?.Level, Is.EqualTo(7));
    }
}
