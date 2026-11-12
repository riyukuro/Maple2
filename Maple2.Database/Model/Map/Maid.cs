using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// A maid, keyed by the uid the client knows its furnishing by: the cube uid while the furnishing is placed, the
// furnishing storage item uid while it is stored (the client's CUserMaidManager and the maid item tooltip look a
// maid up by that uid). No key to the cube table: the row moves to the item when the cube is picked up, and the
// paths that delete cubes without returning them to storage remove their maid rows themselves.
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
    // The running craft (MaidCraftItem): recipe id (0: none), start in epoch seconds, lead time in seconds.
    public int CraftRecipeId { get; set; }
    public long CraftStartTime { get; set; }
    public int CraftDuration { get; set; }

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
            CraftRecipeId = other.CraftRecipeId,
            CraftStartTime = other.CraftStartTime,
            CraftDuration = other.CraftDuration,
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
            CraftRecipeId = other.CraftRecipeId,
            CraftStartTime = other.CraftStartTime,
            CraftDuration = other.CraftDuration,
        };
    }

    public static void Configure(EntityTypeBuilder<Maid> builder) {
        builder.ToTable("maid");
        builder.HasKey(maid => maid.Id);
        builder.Property(maid => maid.Id).ValueGeneratedNever();
        builder.HasIndex(maid => maid.OwnerId);
        builder.Property(maid => maid.Profile).HasJsonConversion().IsRequired();
    }
}
