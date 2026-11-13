using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class BingoEventRulesTests {
    // The client's ShuffleWithSeed with the game's seeded generator (worked out by hand from questmasktable.cpp).
    [Test]
    public void Board_IsTheClientShuffle() {
        Assert.That(BingoEventRules.Board(12345), Is.EqualTo(new[] {
            17, 2, 10, 16, 1, 3, 7, 4, 20, 14, 21, 23, 9, 12, 13, 11, 19, 8, 24, 22, 18, 15, 25, 6, 5,
        }));
        Assert.That(BingoEventRules.Board(-7), Is.EqualTo(new[] {
            25, 4, 9, 10, 20, 13, 12, 7, 1, 22, 11, 3, 17, 16, 19, 6, 24, 15, 5, 18, 2, 14, 23, 8, 21,
        }));
    }

    [Test]
    public void CountLines_RowsColumnsAndDiagonals() {
        int[] board = BingoEventRules.Board(12345);
        Assert.That(BingoEventRules.CountLines(12345, board.Take(5).ToList()), Is.EqualTo(1));
        Assert.That(BingoEventRules.CountLines(12345, Enumerable.Range(0, 5).Select(i => board[i * 5]).ToList()), Is.EqualTo(1));
        Assert.That(BingoEventRules.CountLines(12345, Enumerable.Range(0, 5).Select(i => board[i * 6]).ToList()), Is.EqualTo(1));
        Assert.That(BingoEventRules.CountLines(12345, Enumerable.Range(0, 5).Select(i => board[i * 4 + 4]).ToList()), Is.EqualTo(1));
        Assert.That(BingoEventRules.CountLines(12345, board.Take(24).ToList()), Is.EqualTo(9));
        Assert.That(BingoEventRules.CountLines(12345, board.ToList()), Is.EqualTo(12));
    }

    // Row i needs i + 1 lines, the last row all 12; nothing without 8 rows.
    [Test]
    public void CanClaim_LineCounts() {
        BingoEvent bingo = new(1, 2, [[1]], Enumerable.Range(0, 8).Select(_ => new BingoEvent.BingoReward([])).ToArray());
        Assert.That(BingoEventRules.CanClaim(bingo, 0, 0), Is.False);
        Assert.That(BingoEventRules.CanClaim(bingo, 0, 1), Is.True);
        Assert.That(BingoEventRules.CanClaim(bingo, 6, 7), Is.True);
        Assert.That(BingoEventRules.CanClaim(bingo, 7, 11), Is.False);
        Assert.That(BingoEventRules.CanClaim(bingo, 7, 12), Is.True);
        Assert.That(BingoEventRules.CanClaim(bingo, 8, 12), Is.False);
        Assert.That(BingoEventRules.CanClaim(bingo with { Rewards = bingo.Rewards[..7] }, 0, 12), Is.False);
    }
}
