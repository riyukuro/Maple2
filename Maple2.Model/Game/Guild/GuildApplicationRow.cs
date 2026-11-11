namespace Maple2.Model.Game;

// A stored guild join request: who asked which guild, and when (epoch seconds).
public record GuildApplicationRow(long Id, long GuildId, long ApplicantId, long CreationTime);
