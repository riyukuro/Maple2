using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// A game event user value that belongs to the account, not to one character: it is kept when a character is
// deleted. Used for the claims of a stamp card, whose quests are the account's.
internal class AccountEventValue {
    public long AccountId { get; set; }
    public GameEventUserValueType Type { get; set; }
    public required string Value { get; set; }
    public int EventId { get; set; }
    public long ExpirationTime { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator AccountEventValue?(Maple2.Model.Game.GameEventUserValue? other) {
        return other == null ? null : new AccountEventValue {
            Type = other.Type,
            Value = other.Value,
            EventId = other.EventId,
            ExpirationTime = other.ExpirationTime,
        };
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.GameEventUserValue?(AccountEventValue? other) {
        return other == null ? null : new Maple2.Model.Game.GameEventUserValue(other.Value) {
            Type = other.Type,
            EventId = other.EventId,
            ExpirationTime = other.ExpirationTime,
        };
    }

    public static void Configure(EntityTypeBuilder<AccountEventValue> builder) {
        builder.ToTable("account-event-value");
        builder.HasKey(value => new { value.AccountId, value.EventId, value.Type });
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(value => value.AccountId);
    }
}
