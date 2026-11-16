using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// Achieve 4 (favorite): the star of a trophy the character holds is set to what the client sent; a trophy the
// character does not hold is ignored. Made-up trophy id.
public class TrophyFavoriteTests {
    private const int Trophy = 90000021;

    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.killcount, [1000]));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return session;
    }

    private static void Toggle(GameSession session, int id, bool favorite) {
        new AchievementHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(id);
            writer.WriteBool(favorite);
        }));
    }

    [Test]
    public void Toggle_SetsAndClearsTheStarOfAHeldTrophy() {
        GameSession session = Start();
        session.Exp.OnKill(10, 1000);
        session.Achievement.TryGetAchievement(Trophy, out Achievement? trophy);

        Toggle(session, Trophy, true);
        bool starred = trophy!.Favorite;
        Toggle(session, Trophy, false);

        Assert.Multiple(() => {
            Assert.That(starred, Is.True);
            Assert.That(trophy.Favorite, Is.False);
        });
    }

    [Test]
    public void Toggle_OfATrophyNotHeldChangesNothing() {
        GameSession session = Start();

        Toggle(session, Trophy, true);
        Toggle(session, 12345, true);

        Assert.That(session.Achievement.TryGetAchievement(12345, out _), Is.False);
    }
}
