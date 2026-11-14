using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per world champion season (WorldChampionBase.xml): the step whose npc is out and the damage dealt to it,
// kept by the World server across restarts.
internal class WorldChampionSeason {
    public int Season { get; set; }
    public int Step { get; set; }
    public long StepDamage { get; set; }

    public static void Configure(EntityTypeBuilder<WorldChampionSeason> builder) {
        builder.ToTable("world-champion-season");
        builder.HasKey(season => season.Season);
        builder.Property(season => season.Season).ValueGeneratedNever();
    }
}

// One row per season and character: the damage the character dealt to the season's npcs, and whether the season's
// reward mail was sent to it. The rows go with the character.
internal class WorldChampionDamage {
    public int Season { get; set; }
    public long CharacterId { get; set; }
    public long AccountId { get; set; }
    public long Damage { get; set; }
    public bool Rewarded { get; set; }

    public static void Configure(EntityTypeBuilder<WorldChampionDamage> builder) {
        builder.ToTable("world-champion-damage");
        builder.HasKey(damage => new { damage.Season, damage.CharacterId });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(damage => damage.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
