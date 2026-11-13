using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// A friend notice kept for an offline player: the other character's name, what it did and the entry it was about.
public record BuddyNotice(string Name, BuddyNoticeType Type, long EntryId);
