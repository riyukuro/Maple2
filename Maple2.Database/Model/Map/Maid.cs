using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// A placed maid, keyed by its furnishing cube's uid; the row goes with the cube.
internal class Maid {
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public int ItemId { get; set; }
    public int MaidId { get; set; }
    public int Mood { get; set; }
    public int Level { get; set; }
    public int Exp { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime ExpiryTime { get; set; }
    public required Maple2.Model.Game.MaidProfile Profile { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maid?(Maple2.Model.Game.Maid? other) {
        return other == null ? null : new Maid {
            Id = other.Uid,
            OwnerId = other.OwnerId,
            ItemId = other.ItemId,
            MaidId = other.MaidId,
            Mood = other.Mood,
            Level = other.Level,
            Exp = other.Exp,
            CreationTime = other.CreationTime.FromEpochSeconds(),
            ExpiryTime = other.ExpiryTime.FromEpochSeconds(),
            Profile = other.Profile,
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.Maid?(Maid? other) {
        return other == null ? null : new Maple2.Model.Game.Maid {
            Uid = other.Id,
            OwnerId = other.OwnerId,
            ItemId = other.ItemId,
            MaidId = other.MaidId,
            Mood = other.Mood,
            Level = other.Level,
            Exp = other.Exp,
            CreationTime = other.CreationTime.ToEpochSeconds(),
            ExpiryTime = other.ExpiryTime.ToEpochSeconds(),
            Profile = other.Profile,
        };
    }

    public static void Configure(EntityTypeBuilder<Maid> builder) {
        builder.ToTable("maid");
        builder.HasKey(maid => maid.Id);
        builder.Property(maid => maid.Id).ValueGeneratedNever();
        builder.HasOne<UgcMapCube>()
            .WithOne()
            .HasForeignKey<Maid>(maid => maid.Id)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(maid => maid.OwnerId);
        builder.Property(maid => maid.Profile).HasJsonConversion().IsRequired();
    }
}
