using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// A report a player sent (RequestReport), as the GM report command reads it. Detail is the report's flag and what
// it points at (the chat line, the home, the pet, the poster or the listing). CloseTime is null while it is open.
public record PlayerReportEntry(
    long Id,
    ReportCategory Category,
    long CharacterId,
    string PlayerName,
    long ReporterCharacterId,
    string ReporterName,
    string Reason,
    string Detail,
    DateTime CreateTime,
    DateTime? CloseTime,
    long ClosedBy);
