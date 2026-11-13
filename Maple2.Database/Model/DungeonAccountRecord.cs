using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// The reward counts of an account reward room (dungeonroom.xml isAccountReward): the clears, the timed clears, the
// bought and the event counts with their period ends, kept for the account so that every character of it counts
// against the same limit. The character's dungeon-record keeps the rest (records, total clears, flags) and a copy of
// these counts.
internal class DungeonAccountRecord {
    public long AccountId { get; set; }
    public int DungeonId { get; set; }
    public byte CurrentSubClears { get; set; }
    public byte CurrentClears { get; set; }
    public byte ExtraCurrentSubClears { get; set; }
    public byte ExtraCurrentClears { get; set; }
    public DateTime ExtraCooldownTime { get; set; }
    public DateTime DailyResetTime { get; set; }
    public DateTime UnionCooldownTime { get; set; }

    public static DungeonAccountRecord From(long accountId, Maple2.Model.Game.Dungeon.DungeonRecord record) {
        return new DungeonAccountRecord {
            AccountId = accountId,
            DungeonId = record.DungeonId,
            CurrentSubClears = record.UnionSubClears,
            CurrentClears = record.UnionClears,
            ExtraCurrentSubClears = record.ExtraSubClears,
            ExtraCurrentClears = record.ExtraClears,
            ExtraCooldownTime = record.ExtraCooldownTimestamp.FromEpochSeconds(),
            DailyResetTime = record.UnionSubCooldownTimestamp.FromEpochSeconds(),
            UnionCooldownTime = record.UnionCooldownTimestamp.FromEpochSeconds(),
        };
    }

    // The counts into a character's record of the room.
    public void CopyTo(Maple2.Model.Game.Dungeon.DungeonRecord record) {
        record.UnionSubClears = CurrentSubClears;
        record.UnionClears = CurrentClears;
        record.ExtraSubClears = ExtraCurrentSubClears;
        record.ExtraClears = ExtraCurrentClears;
        record.ExtraCooldownTimestamp = ExtraCooldownTime.ToEpochSeconds();
        record.UnionSubCooldownTimestamp = DailyResetTime.ToEpochSeconds();
        record.UnionCooldownTimestamp = UnionCooldownTime.ToEpochSeconds();
    }

    public static void Configure(EntityTypeBuilder<DungeonAccountRecord> builder) {
        builder.ToTable("dungeon-account-record");
        builder.HasKey(record => new { record.AccountId, record.DungeonId });
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(record => record.AccountId);
    }
}
