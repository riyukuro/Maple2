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

    // The passive skills' effects a married character holds (weddingskill.xml type passive): each skill whose
    // requireGrade the grade reaches, at the client's level (WeddingSkillData::GetLevel: grade - requireGrade + 1, at
    // most maxCount + 1, as the skill page shows it, uiweddingpage.cpp:250). The parser has no maxCount; every KR row's
    // effect has exactly maxCount + 1 levels, so the level is capped at the highest one `hasLevel` finds. A marriage
    // counts while its exp counts (MarriageExpRules.CountsWeddingExp: married, a filed divorce in its cooling off).
    public static List<(int Id, short Level)> PassiveEffects(MaritalStatus status, long exp, WeddingTable table, Func<int, short, bool> hasLevel) {
        var effects = new List<(int Id, short Level)>();
        if (!MarriageExpRules.CountsWeddingExp(status) || table.Skills == null) {
            return effects;
        }
        int grade = Grade(exp, table.Grades);
        foreach (WeddingSkill skill in table.Skills.Values.OrderBy(skill => skill.Id)) {
            if (skill.Type != WeddingSkillType.Passive || skill.AdditionalEffectId <= 0 || grade < skill.RequireGrade) {
                continue;
            }
            int level = Math.Min(grade - skill.RequireGrade + 1, short.MaxValue);
            while (level > 0 && !hasLevel(skill.AdditionalEffectId, (short) level)) {
                level--;
            }
            if (level > 0) {
                effects.Add((skill.AdditionalEffectId, (short) level));
            }
        }
        return effects;
    }
}
