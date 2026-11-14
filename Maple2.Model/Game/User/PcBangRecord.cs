namespace Maple2.Model.Game;

// The account's PC bang day (PcBangRules): the day as yyyyMMdd, the milliseconds played at a PC bang that day, the
// play bonus rows claimed (bit i = row i, as the client's bitset) and the gift ids received. A record of an earlier
// day counts as none (the daily reset starts the day over).
public record PcBangRecord(int Day, long PlayMs, int Claimed, IReadOnlyList<int> Gifts);
