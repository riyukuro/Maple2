using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;

namespace Maple2.Database.Model;

internal class DungeonRankReward {
    public int Id { get; set; }
    public int RankClaimed { get; set; }
    public DateTime ClaimTime { get; set; } // the expiry (the season week's end), stored under its old name

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator DungeonRankReward?(Maple2.Model.Game.Dungeon.DungeonRankReward? other) {
        return other == null ? null : new DungeonRankReward {
            Id = other.Id,
            RankClaimed = other.RankClaimed,
            ClaimTime = other.ExpiryTimestamp.FromEpochSeconds(),
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.Dungeon.DungeonRankReward?(DungeonRankReward? other) {
        return other == null ? null : new Maple2.Model.Game.Dungeon.DungeonRankReward(other.Id) {
            RankClaimed = other.RankClaimed,
            ExpiryTimestamp = other.ClaimTime.ToEpochSeconds(),
        };
    }
}
