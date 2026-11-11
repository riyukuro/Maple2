using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per feeding of an own pet or play with another account's pet. The daily reset empties the table.
internal class NurturingDaily {
    public long Id { get; set; }
    public long AccountId { get; set; } // the account that fed or played
    public long OwnerId { get; set; } // the pet's owner (AccountId of the nurturing row)
    public int ItemId { get; set; } // the pet item (InteractId of the nurturing row)
    public bool Played { get; set; } // false: fed, true: played

    public static void Configure(EntityTypeBuilder<NurturingDaily> builder) {
        builder.ToTable("nurturing-daily");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.AccountId);
    }
}
