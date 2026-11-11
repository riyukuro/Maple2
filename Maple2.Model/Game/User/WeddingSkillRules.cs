using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public static class WeddingSkillRules {
    // Client WeddingTable::GetGrade: the grade before the first row (in grade order) whose exp is above the
    // marriage exp; past every row, the highest grade. An empty table gives 0.
    public static int Grade(long exp, IReadOnlyDictionary<int, long>? grades) {
        if (grades == null || grades.Count == 0) {
            return 0;
        }
        foreach ((int grade, long value) in grades.OrderBy(entry => entry.Key)) {
            if (value > exp) {
                return grade - 1;
            }
        }
        return grades.Keys.Max();
    }

    // Wedding 37 (UIWeddingPageSkill onReceive, uiweddingpage.cpp:310): the item of a skill of type item, for a
    // grade that reaches the skill's requireGrade and a character that does not hold the item (the client's own
    // test). Only a married couple; the client sends it from the wedding window, which this server rule keeps
    // to state 2.
    public static WeddingSkill? ItemToGive(int skillId, MaritalStatus status, long exp, WeddingTable table, Func<int, bool> holds) {
        if (status != MaritalStatus.Married) {
            return null;
        }
        if (table.Skills == null || !table.Skills.TryGetValue(skillId, out WeddingSkill? skill)) {
            return null;
        }
        if (skill.Type != WeddingSkillType.Item || skill.ItemId <= 0 || skill.ItemAmount <= 0) {
            return null;
        }
        if (Grade(exp, table.Grades) < skill.RequireGrade || holds(skill.ItemId)) {
            return null;
        }
        return skill;
    }
}
