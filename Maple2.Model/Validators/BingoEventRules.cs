using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The bingo card (GameEventBingoEvent), from the client's dialog (uibingoeventdialog.cpp) and BingoContext
// (gameeventcontext.cpp). The board holds the numbers 1-25 shuffled by the card's seed (the int of EventReward 20,
// CountLines -> ShuffleWithSeed); a cell is crossed when its number is in the checked list. The lines are the 5
// rows, the 5 columns and the 2 diagonals. Reward row i (0-7) is offered once i + 1 lines are crossed, the last
// row (7) only with all 12, and only when the event has 8 rows. The free pencil crosses only a number called
// today, the cash pencil any number of the board.
public static class BingoEventRules {
    public const int BoardSize = 5;
    public const int LineCount = 12;
    public const int RewardRows = 8;

    // ShuffleWithSeed (buffercryptmanager.cpp): Fisher-Yates from the back, with the game's seeded generator.
    public static int[] Board(int seed) {
        int[] board = Enumerable.Range(1, BoardSize * BoardSize).ToArray();
        var random = new SeededRandom(seed);
        for (int i = board.Length - 1; i >= 1; i--) {
            int j = (int) (random.Next() % (uint) (i + 1));
            if (i != j) {
                (board[i], board[j]) = (board[j], board[i]);
            }
        }
        return board;
    }

    public static int CountLines(int seed, ICollection<int> checkedNumbers) {
        int[] board = Board(seed);
        var cells = new bool[BoardSize, BoardSize];
        for (int i = 0; i < board.Length; i++) {
            cells[i / BoardSize, i % BoardSize] = checkedNumbers.Contains(board[i]);
        }

        int lines = 0;
        bool diagonal = true;
        bool antiDiagonal = true;
        for (int a = 0; a < BoardSize; a++) {
            bool row = true;
            bool column = true;
            for (int b = 0; b < BoardSize; b++) {
                row &= cells[a, b];
                column &= cells[b, a];
            }
            lines += (row ? 1 : 0) + (column ? 1 : 0);
            diagonal &= cells[a, a];
            antiDiagonal &= cells[a, BoardSize - 1 - a];
        }
        return lines + (diagonal ? 1 : 0) + (antiDiagonal ? 1 : 0);
    }

    public static bool CanClaim(BingoEvent bingo, int index, int lines) {
        if (bingo.Rewards.Length != RewardRows || index < 0 || index >= RewardRows) {
            return false;
        }
        return index == RewardRows - 1 ? lines >= LineCount : lines >= index + 1;
    }

    // The numbers called today: BingoContext::GetTodayNumbers counts the days from the start date's midnight and
    // wraps them by the number of days in the table.
    public static int[] TodayNumbers(BingoEvent bingo, DateTime start, DateTime now) {
        if (bingo.Numbers.Length == 0) {
            return [];
        }
        int days = Math.Max(0, (now.Date - start.Date).Days);
        return bingo.Numbers[days % bingo.Numbers.Length];
    }

    public static bool OnBoard(int number) => number is >= 1 and <= BoardSize * BoardSize;

    public static List<int> Parse(string? value) {
        return string.IsNullOrEmpty(value) ? [] : value.Split(',').Select(int.Parse).ToList();
    }

    // The game's three-word generator (questmasktable.cpp Unk_Random): the seed is stepped by the C runtime's
    // LCG for the three words, which get their low bits forced.
    private sealed class SeededRandom {
        private uint s1;
        private uint s2;
        private uint s3;

        public SeededRandom(int seed) {
            uint state = unchecked((uint) seed);
            s1 = Step(ref state) | 0x100000;
            s2 = Step(ref state) | 0x1000;
            s3 = Step(ref state) | 0x10;
        }

        private static uint Step(ref uint state) {
            uint value = state;
            state = unchecked(state * 0x343fd + 0x269ec3);
            return value;
        }

        public uint Next() {
            s1 = ((s1 & 0xfffffffe) << 12) ^ (((s1 << 13) ^ s1) >> 19);
            s2 = ((s2 & 0xfffffff8) << 4) ^ (((s2 << 2) ^ s2) >> 25);
            s3 = ((s3 & 0xfffffff0) << 17) ^ (((s3 << 3) ^ s3) >> 11);
            return s1 ^ s2 ^ s3;
        }
    }
}
