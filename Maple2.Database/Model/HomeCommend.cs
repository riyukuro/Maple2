using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per house commendation (RequestCube 0x19): the account that commended, the home's owner and when.
// The rows go with the home and with the commending account.
internal class HomeCommend {
    public long Id { get; set; }
    public long OwnerId { get; set; } // account id of the home's owner
    public long AccountId { get; set; } // the account that commended
    public DateTime Time { get; set; }

    public static void Configure(EntityTypeBuilder<HomeCommend> builder) {
        builder.ToTable("home-commend");
        builder.HasKey(commend => commend.Id);
        builder.HasOne<Home>()
            .WithMany()
            .HasForeignKey(commend => commend.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(commend => commend.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(commend => new { commend.OwnerId, commend.Time });
        builder.HasIndex(commend => new { commend.AccountId, commend.Time });
    }
}
