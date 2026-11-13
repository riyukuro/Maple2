using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per character: the player config the World keeps between logins (PlayerConfigLookUp), so a World restart
// keeps it. Written by the World only, apart from character-config, whose row version the game server's saves use.
internal class CharacterPlayerConfig {
    public long CharacterId { get; set; }
    public List<PlayerConfigBuff> Buffs { get; set; } = [];
    public List<PlayerConfigCooldown> SkillCooldowns { get; set; } = [];
    public List<PlayerConfigCooldown> PvpHeldCooldowns { get; set; } = [];
    public bool PvpStay { get; set; }
    public int DeathCount { get; set; }
    public int DeathMsRemaining { get; set; }
    public long DeathStopTime { get; set; }
    public DateTime LastModified { get; set; }

    public static void Configure(EntityTypeBuilder<CharacterPlayerConfig> builder) {
        builder.ToTable("character-player-config");
        builder.HasKey(config => config.CharacterId);
        builder.HasOne<Character>()
            .WithOne()
            .HasForeignKey<CharacterPlayerConfig>(config => config.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(config => config.Buffs).HasJsonConversion();
        builder.Property(config => config.SkillCooldowns).HasJsonConversion();
        builder.Property(config => config.PvpHeldCooldowns).HasJsonConversion();
    }
}
