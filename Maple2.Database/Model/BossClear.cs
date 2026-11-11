using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per clear of a dungeon room with a boss board (the room's BossRanking): the party leader at the clear,
// the clear time in seconds and when the room was cleared. The leader has no key to the character: a board row whose
// leader is gone shows its first member, as the client does.
internal class BossClear {
    public long Id { get; set; }
    public int RankingId { get; set; }
    public int DungeonId { get; set; }
    public long LeaderId { get; set; }
    public int Seconds { get; set; }
    public DateTime ClearTime { get; set; }
    // The damage the party dealt to the room's bosses (kept for the world boss board, 0 for the other rooms).
    public long Damage { get; set; }
    // Added with the clear, so the clear and its members are saved together.
    public List<BossClearMember>? Members { get; set; }

    public static void Configure(EntityTypeBuilder<BossClear> builder) {
        builder.ToTable("boss-clear");
        builder.HasKey(clear => clear.Id);
        builder.Property(clear => clear.Id).ValueGeneratedOnAdd();
        builder.HasIndex(clear => new { clear.RankingId, clear.ClearTime });
        builder.HasIndex(clear => new { clear.RankingId, clear.Seconds });
    }
}

// The party members of a boss clear. A member goes with its character, and with the clear.
internal class BossClearMember {
    public long ClearId { get; set; }
    public long CharacterId { get; set; }

    public static void Configure(EntityTypeBuilder<BossClearMember> builder) {
        builder.ToTable("boss-clear-member");
        builder.HasKey(member => new { member.ClearId, member.CharacterId });
        builder.HasOne<BossClear>()
            .WithMany(clear => clear.Members)
            .HasForeignKey(member => member.ClearId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(member => member.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(member => member.CharacterId);
    }
}
