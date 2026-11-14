using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Database.Context;

// Column defaults and ON UPDATE (CURRENT_TIMESTAMP) take the session time zone, while the server reads every
// datetime column back as its own local time (FromEpochSeconds, ToEpochSeconds) and sends that zone to the client
// in TimeSync. Each opened connection is set to the server's current UTC offset, so a time the database fills is in
// the same zone as one the server writes. The pool resets session state on reuse, so this runs on every open.
// Chosen here: a fixed offset taken at open time (the server's zone may have no name loaded in MySQL).
public sealed class LocalTimeZoneInterceptor : DbConnectionInterceptor {
    public static readonly LocalTimeZoneInterceptor Instance = new();

    public static string Offset(DateTime utcNow) {
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(utcNow);
        return $"{(offset < TimeSpan.Zero ? '-' : '+')}{offset.Duration():hh\\:mm}";
    }

    private static DbCommand Command(DbConnection connection) {
        DbCommand command = connection.CreateCommand();
        command.CommandText = $"SET time_zone = '{Offset(DateTime.UtcNow)}'";
        return command;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) {
        using DbCommand command = Command(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default) {
        await using DbCommand command = Command(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
