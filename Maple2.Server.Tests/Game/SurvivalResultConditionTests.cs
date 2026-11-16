using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// The quest conditions of one Mushking Royale match, from real KR rows, through SurvivalGameField: the kill's (the
// double kill) at the fall, the match's counts with the result (SurvivalConditions.Result) at the game's end.
public class SurvivalResultConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Skill = 90000783;

    private static ConditionMetadata Row(ConditionType type, int[]? codes = null, int[]? targets = null) =>
        new(type, 1, codes == null ? null : new ConditionMetadata.Parameters(Integers: codes),
            targets == null ? null : new ConditionMetadata.Parameters(Integers: targets));

    // The winner kills the three others with one skill, two of them in the same tick. With "busy" it also killed
    // ten monsters, broke ten breakable objects and used an interact object before.
    [TestCase(false)]
    [TestCase(true)]
    public void Match_TheResultsCounts(bool busy) {
        MapMetadata map = TestGameSession.Map(82000000);
        var winner = new TestGameSession();
        winner.AddMap(map);
        // The result's game and survival exp read these tables (no rows here: none given, no season).
        winner.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        winner.SetTable(new MapleSurvivalTable(new Dictionary<int, MapleSurvivalTable.Period>(), new Dictionary<int, long> { [1] = 0 },
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>()));
        winner.Start();
        // The result's lobby resend loads the survival record and skins.
        winner.Session.Survival = new Maple2.Server.Game.Manager.SurvivalManager(winner.Session);
        winner.EnterMap(map);
        FieldManager field = winner.Session.Field!;
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(field, new Dictionary<string, Maple2.Server.Game.Model.Widget.Widget>());
        var others = new List<TestGameSession>();
        for (int i = 0; i < 3; i++) {
            var other = new TestGameSession(winner.Options);
            other.Start();
            typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, 2 + i);
            field.Players[2 + i] = other.Session.Player;
            other.Session.Field = field;
            others.Add(other);
        }
        Quest quest = winner.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [
                // 21300080 survival_kill target 3; 21300042 target 10.
                Row(ConditionType.survival_kill, targets: [3]),
                Row(ConditionType.survival_kill, targets: [10]),
                // 21300071 survival_kill_use_skill, its six skills, target 1; 21300043 code 90000772 target 5.
                Row(ConditionType.survival_kill_use_skill, [90000729, 90000730, 90000801, 90000783, 90000784, 90000785], [1]),
                Row(ConditionType.survival_kill_use_skill, [90000772], [5]),
                // 21300049 survival_win_use_one_skill code 90000783; 21300047 code 90000730.
                Row(ConditionType.survival_win_use_one_skill, [Skill]),
                Row(ConditionType.survival_win_use_one_skill, [90000730]),
                // 21300072 survival_double_kill.
                Row(ConditionType.survival_double_kill),
                // 21300082 survival_npc_kill target 10; 21300060 survival_breakable_object target 10.
                Row(ConditionType.survival_npc_kill, targets: [10]),
                Row(ConditionType.survival_breakable_object, targets: [10]),
                // 21300044 survival_win_without_interact; 21300045 survival_win_without_npckill.
                Row(ConditionType.survival_win_without_interact),
                Row(ConditionType.survival_win_without_npckill),
            ],
        });
        var game = new SurvivalGameField(field, [winner.Session.Player, .. others.Select(other => other.Session.Player)]);
        if (busy) {
            for (int i = 0; i < 10; i++) {
                game.NpcKill(winner.Session.Player);
                game.Breakable(winner.Session.Player);
            }
            game.Interact(winner.Session.Player);
        }

        foreach (TestGameSession other in others) {
            game.Damage(winner.Session.Player, other.Session.Player, Skill);
        }
        foreach (TestGameSession other in others) {
            game.Death(other.Session.Player);
        }

        Assert.That(game.Ended, Is.True);
        Assert.That(winner.Errors, Is.Empty);
        long[] expected = busy
            ? [1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 0]
            : [1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1];
        Assert.That(quest.Conditions.Values.Select(condition => condition.Counter), Is.EqualTo(expected));
    }
}
