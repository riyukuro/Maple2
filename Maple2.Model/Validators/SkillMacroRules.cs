namespace Maple2.Model.Validators;

// SkillMacro mode 1: i32 tab count, then per tab its name, i64 key, i32 skill count and the skill ids.
public static class SkillMacroRules {
    // The client opens its add box only below 3 tabs and checks the count again on the box's answer.
    public const int MaxTabs = 3;
    // The add and rename boxes take 5 characters.
    public const int MaxNameLength = 5;
    // The client makes a tab itself only when the server's list is empty, so that tab is the first one. Its name
    // is s_skill_compact_control_default_tabname, which no box limits: 16 is the longest in the client's languages.
    public const int MaxDefaultNameLength = 16;

    // The client removes a tab without asking how many are left (RemoveTab, uiskillcompactcontroldialog.cpp:910) and
    // saves its list on every close (vfunc_67, :154), also before the load answer came, when the list is still empty.
    // An empty list is taken only once EmptySaveGraceMs have passed since the server answered the load: a player who
    // removed the last tab clicked after the tabs arrived, while an early close comes within a round trip. A list
    // the server stored as empty gets the client's default tab on the next load (vfunc_79, :523).
    public const long EmptySaveGraceMs = 2000;

    public static bool IsTabCount(int count, long loadAnsweredTick, long nowTick) {
        if (count == 0) {
            return loadAnsweredTick != 0 && nowTick - loadAnsweredTick >= EmptySaveGraceMs;
        }
        return count is >= 1 and <= MaxTabs;
    }

    public static int NameLimit(int index) {
        return index == 0 ? MaxDefaultNameLength : MaxNameLength;
    }

    // The ids are four bytes each, so a count the rest of the packet cannot hold is refused before any is read.
    public static bool IsSkillCount(int count, int bytesLeft) {
        return count >= 0 && count <= bytesLeft / 4;
    }

    // The client refuses an empty name and a name another tab already has; it compares without case.
    public static bool IsNewName(string name, IReadOnlyCollection<string> names) {
        return name.Length > 0 && name.Length <= NameLimit(names.Count)
            && !names.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    // The client keeps a tab's skills in an ordered set and writes it front to back: ids rise and never repeat.
    public static bool IsNextSkill(int previous, int skillId) {
        return skillId > 0 && skillId > previous;
    }
}
