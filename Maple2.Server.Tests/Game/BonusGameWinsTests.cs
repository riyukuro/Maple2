using System.Collections.Generic;
using System.Numerics;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// The bonus_game_reward condition's store: a notice win is seen once, only while its player is in the box; the wins
// of players who left are dropped; another type is not a notice win.
public class BonusGameWinsTests {
    private readonly Dictionary<long, Vector3> players = [];

    private Vector3? Position(long characterId) => players.TryGetValue(characterId, out Vector3 position) ? position : null;

    private static bool InBox(Vector3 position) => position.X < 100;

    [Test]
    public void WinSeenOnceInTheBox() {
        var wins = new BonusGameWins();
        players[1] = new Vector3(500, 0, 0);
        wins.Add(1, BonusGameWins.NoticeType);
        Assert.Multiple(() => {
            Assert.That(wins.Take(BonusGameWins.NoticeType, Position, InBox), Is.False);
            Assert.That(wins.Count, Is.EqualTo(1));
        });
        players[1] = new Vector3(50, 0, 0);
        Assert.Multiple(() => {
            Assert.That(wins.Take(2, Position, InBox), Is.False);
            Assert.That(wins.Take(BonusGameWins.NoticeType, Position, InBox), Is.True);
            Assert.That(wins.Take(BonusGameWins.NoticeType, Position, InBox), Is.False);
        });
    }

    [Test]
    public void LeftPlayersAreDroppedAndTheStoreIsCapped() {
        var wins = new BonusGameWins();
        wins.Add(2, BonusGameWins.NoticeType);
        Assert.Multiple(() => {
            Assert.That(wins.Take(BonusGameWins.NoticeType, Position, InBox), Is.False);
            Assert.That(wins.Count, Is.EqualTo(0));
        });
        for (int i = 0; i < BonusGameWins.MaxWins + 5; i++) {
            wins.Add(3, BonusGameWins.NoticeType);
        }
        Assert.That(wins.Count, Is.EqualTo(BonusGameWins.MaxWins));
    }
}
