namespace Maple2.Model.Game;

// A guild expel kept for a character that was offline when it was expelled: the expeller's name and when (unix seconds).
public record GuildExpelNotice(string Name, long Time);
