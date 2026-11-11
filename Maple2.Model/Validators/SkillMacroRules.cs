namespace Maple2.Model.Validators;

// SkillMacro mode 1: i32 tab count, then per tab its name, i64 key, i32 skill count and the skill ids.
public static class SkillMacroRules {
    // The client opens its add box only below 3 tabs and checks the count again on the box's answer.
    public const int MaxTabs = 3;
    // The add and rename boxes take 5 characters. The first tab is made by the client with a name from its own
    // strings, which no box limits, so the server's bound is wider than the boxes'.
    public const int MaxNameLength = 20;

    // The client always holds at least one tab: it makes the default one when the server's list is empty.
    public static bool IsTabCount(int count) {
        return count is >= 1 and <= MaxTabs;
    }

    // The ids are four bytes each, so a count the rest of the packet cannot hold is refused before any is read.
    public static bool IsSkillCount(int count, int bytesLeft) {
        return count >= 0 && count <= bytesLeft / 4;
    }

    // The client refuses an empty name and a name another tab already has.
    public static bool IsNewName(string name, IEnumerable<string> names) {
        return name.Length is > 0 and <= MaxNameLength && !names.Contains(name);
    }

    // The client keeps a tab's skills in an ordered set and writes it front to back: ids rise and never repeat.
    public static bool IsNextSkill(int previous, int skillId) {
        return skillId > 0 && skillId > previous;
    }
}
