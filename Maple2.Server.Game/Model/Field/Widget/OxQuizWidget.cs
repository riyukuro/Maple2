using System.Collections.Concurrent;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Model.Widget;

public class OxQuizWidget : Widget {

    private bool isDevMode;
    private int round;
    private readonly ConcurrentDictionary<int, OxQuizTable.Entry> questions; // round, question

    public OxQuizWidget(FieldManager field) : base(field) {
        Conditions = new ConcurrentDictionary<string, int>();
        questions = new ConcurrentDictionary<int, OxQuizTable.Entry>();
    }

    public override bool Check(string name, string arg) {
        return Conditions.GetValueOrDefault(name) == 1;
    }

    public override void Action(string function, int numericArg, string stringArg) {
        switch (function) {
            case "DevMode":
                DevMode(stringArg, numericArg);
                break;
            case "PickQuiz":
                PickQuiz(stringArg, numericArg);
                break;
            case "ShowQuiz":
                ShowQuiz(stringArg, numericArg);
                break;
            case "ShowAnswer":
                ShowAnswer(stringArg, numericArg);
                break;
            case "PreJudge":
                PreJudge(stringArg, numericArg);
                break;
            case "Judge":
                Judge(stringArg, numericArg);
                break;
            case "Winner":
                Winner(stringArg, numericArg);
                break;
            default:
                Log.Logger.Warning("Unknown function called on OxQuizWidget: {Function}", function);
                break;
        }
    }

    // The answer in the question text is the script's choice alone (61000003 sets DevMode 0), debug build or not.
    private void DevMode(string isOnStr, int numericArg) {
        isDevMode = isOnStr == "1";
    }

    private void PickQuiz(string levelStr, int numericArg) {
        round++;
        if (!int.TryParse(levelStr, out int level)) {
            level = 1;
        }

        OxQuizTable.Entry? question = Pick(Field.ServerTableMetadata.OxQuizTable.Entries.Values, level,
            questions.Values.Select(asked => asked.Id).ToHashSet(), Random.Shared);
        if (question != null) {
            questions[round] = question;
        }
    }

    // A random question of the level not yet asked in this game. Chosen here: once the level has no unasked question
    // left, any question of the level again (null: the level has none).
    internal static OxQuizTable.Entry? Pick(IEnumerable<OxQuizTable.Entry> entries, int level, IReadOnlySet<int> asked, Random random) {
        List<OxQuizTable.Entry> ofLevel = entries.Where(question => question.Level == level).ToList();
        List<OxQuizTable.Entry> unasked = ofLevel.Where(question => !asked.Contains(question.Id)).ToList();
        List<OxQuizTable.Entry> pool = unasked.Count > 0 ? unasked : ofLevel;
        return pool.Count > 0 ? pool[random.Next(pool.Count)] : null;
    }

    private void ShowQuiz(string durationStr, int numericArg) {
        if (!questions.TryGetValue(round, out OxQuizTable.Entry? question)) {
            return;
        }
        string answer = string.Empty;
        if (isDevMode) {
            answer = question.IsTrue ? "\n[DebugMode] TRUE" : "\n[DebugMode] FALSE";
        }
        if (!int.TryParse(durationStr, out int duration)) {
            duration = 15;
        }
        Field.Broadcast(QuizEventPacket.Question(question.Category, question.Question, answer, duration));
    }

    private void ShowAnswer(string durationStr, int numericArg) {
        if (!questions.TryGetValue(round, out OxQuizTable.Entry? question)) {
            return;
        }
        if (!int.TryParse(durationStr, out int duration)) {
            duration = 15;
        }
        Field.Broadcast(QuizEventPacket.Answer(question.IsTrue, question.Answer, duration));
    }

    private void PreJudge(string stringArg, int numericArg) {
        if (!questions.TryGetValue(round, out OxQuizTable.Entry? question)) {
            return;
        }
        // The game's events are the channel's: any player here finds them (no player, no one to judge).
        bool reversed = Field.Players.Values.FirstOrDefault()?.Session.FindEvent(GameEventType.ReverseOxQuiz) is { Count: > 0 };
        bool oWins = OWins(question.IsTrue, reversed);
        Conditions["Correct"] = oWins ? 1 : 0;
        Conditions["Incorrect"] = oWins ? 0 : 1;
    }

    // The script's "Correct" branch makes the O box (101) the round's winner, "Incorrect" the X box (102). A running
    // ReverseOxQuiz event (April Fools, "today the wrong answer is right") makes the false side win. The answer the
    // players are shown (ShowAnswer) stays the true one: the client's panel marks it and shows its explanation.
    internal static bool OWins(bool isTrue, bool reversed) => isTrue != reversed;

    private void Judge(string stringArg, int numericArg) { }

    private void Winner(string stringArg, int numericArg) { }
}
