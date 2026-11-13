using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// The live ban-word list (DynamicBanWordRules), words added by GMs.
internal class DynamicBanWord {
    public int Id { get; set; }
    public int Type { get; set; }
    public required string Word { get; set; }
    public required string Replacement { get; set; }
    public long AddedBy { get; set; }
    public DateTime CreationTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.DynamicBanWord?(DynamicBanWord? other) {
        return other == null ? null : new Maple2.Model.Game.DynamicBanWord {
            Id = other.Id,
            Type = other.Type,
            Word = other.Word,
            Replacement = other.Replacement,
        };
    }

    public static void Configure(EntityTypeBuilder<DynamicBanWord> builder) {
        builder.ToTable("dynamic-ban-word");
        builder.HasKey(word => word.Id);
        builder.Property(word => word.Word).HasMaxLength(100).HasColumnType("varchar(100)");
        builder.Property(word => word.Replacement).HasMaxLength(100).HasColumnType("varchar(100)");
    }
}
