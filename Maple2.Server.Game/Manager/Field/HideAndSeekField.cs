using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// The steps of a Hide and Seek game, named as the user values its script waits for (61000023_main: each state moves
// on when the server sets its key to 1).
public enum HideAndSeekStep {
    WaitForEnterUser,
    GameRuleNotice,
    DivideIntoTeams,
    MoveGameArea,
    BeInHidingTeams,
    LookingForATeams,
    TeamMatchResult,
    GameExitNotice,
    ShortOfUser,
    End,
}

// The rules of a Hide and Seek game, without the field: the script's constants (field_game_constant), the step and
// when it began, and the teams. The script's states only allow these moves: WaitForEnterUser to GameRuleNotice or
// ShortOfUser; GameRuleNotice to DivideIntoTeams; DivideIntoTeams and MoveGameArea to the next step or ShortOfUser;
// BeInHidingTeams to LookingForATeams or TeamMatchResult; LookingForATeams to TeamMatchResult; then GameExitNotice
// and End; ShortOfUser to End.
public sealed class HideAndSeekState {
    public const byte HideTeam = 1;
    public const byte SeekTeam = 2;

    // Every key the hide-and-seek fields give (61000023-61000029). The ones the game does not use yet (catches, exp,
    // invincibility, the watch camera, the events and the rewards) are kept for part 2.
    public static readonly HashSet<string> Keys = [
        "BeginTriggerID", "EndTriggerID", "RequireUserCount", "EnterableSeconds", "GameRuleNoticeTick", "ShortOfUserTick",
        "DivideTeamsSeconds", "MoveGameAreaTick", "BeInHidingTeamsSeconds", "LookingForATeamsSeconds", "TeamMatchResultTick",
        "GameExitNoticeTick", "HideTeamExp", "HideTeamExpDurationTick", "SeekTeamExp", "SeekTeamInvincibleTick",
        "InvincibleNoticeTick", "InvincibleMessage", "PortalWaitAreaID", "PortalGameAreaID", "PortalFailAreaID",
        "SeekTeamSkillSetID", "WatchCameraID", "EventDailyQuestStart", "EventHideAndSeekStart", "EventHideAndSeekWin",
        "WinnerRewardItemID", "WinnerRewardItemRank", "WinnerRewardItemCount", "LoserRewardItemID", "LoserRewardItemRank",
        "LoserRewardItemCount",
    ];

    private readonly ILogger logger;
    private readonly Dictionary<string, string> constants = [];
    private readonly Dictionary<long, byte> teams = [];

    public HideAndSeekStep Step { get; private set; }
    public long StepTick { get; private set; }
    // The team that won (TeamMatchResult on): the hiders when one is left at the end, else the seekers.
    public byte Winner { get; private set; }

    public IReadOnlyDictionary<long, byte> Teams => teams;

    public HideAndSeekState(long tick, ILogger? logger = null) {
        this.logger = logger ?? Log.Logger.ForContext<HideAndSeekState>();
        Step = HideAndSeekStep.WaitForEnterUser;
        StepTick = tick;
    }

    // A field_game_constant, kept when its region is ALL or the server's. A key no hide-and-seek field gives is
    // ignored with one Debug line. The ingest already dropped the values of a disabled feature; the region tag stays
    // (the KR, NA and CN rewards). Returns whether the value was kept.
    public bool SetConstant(string key, string value, string locale, string serverLocale) {
        if (!Keys.Contains(key)) {
            logger.Debug("Hide and Seek constant {Key} is not one the game knows; ignored", key);
            return false;
        }
        if (locale != "ALL" && locale != serverLocale) {
            return false;
        }
        constants[key] = value;
        return true;
    }

    public string? Constant(string key) => constants.GetValueOrDefault(key);

    // A missing or unreadable number is 0: a step without its time moves on at once.
    public int Int(string key) => int.TryParse(constants.GetValueOrDefault(key), out int value) ? value : 0;

    public long Duration(HideAndSeekStep step) => step switch {
        HideAndSeekStep.WaitForEnterUser => Int("EnterableSeconds") * 1000L,
        HideAndSeekStep.GameRuleNotice => Int("GameRuleNoticeTick"),
        HideAndSeekStep.DivideIntoTeams => Int("DivideTeamsSeconds") * 1000L,
        HideAndSeekStep.MoveGameArea => Int("MoveGameAreaTick"),
        HideAndSeekStep.BeInHidingTeams => Int("BeInHidingTeamsSeconds") * 1000L,
        HideAndSeekStep.LookingForATeams => Int("LookingForATeamsSeconds") * 1000L,
        HideAndSeekStep.TeamMatchResult => Int("TeamMatchResultTick"),
        HideAndSeekStep.GameExitNotice => Int("GameExitNoticeTick"),
        HideAndSeekStep.ShortOfUser => Int("ShortOfUserTick"),
        _ => long.MaxValue,
    };

    private bool Short(int players) => players < Math.Max(1, Int("RequireUserCount"));

    // The step's time is up: the next step, or null while it runs (and at the end).
    public HideAndSeekStep? Advance(long tick, int players) {
        if (Step == HideAndSeekStep.End || tick - StepTick < Duration(Step)) {
            return null;
        }
        HideAndSeekStep next = Step switch {
            HideAndSeekStep.WaitForEnterUser => Short(players) ? HideAndSeekStep.ShortOfUser : HideAndSeekStep.GameRuleNotice,
            HideAndSeekStep.GameRuleNotice => HideAndSeekStep.DivideIntoTeams,
            HideAndSeekStep.DivideIntoTeams => Short(players) ? HideAndSeekStep.ShortOfUser : HideAndSeekStep.MoveGameArea,
            HideAndSeekStep.MoveGameArea => HideAndSeekStep.BeInHidingTeams,
            HideAndSeekStep.BeInHidingTeams => HideAndSeekStep.LookingForATeams,
            HideAndSeekStep.LookingForATeams => HideAndSeekStep.TeamMatchResult,
            HideAndSeekStep.TeamMatchResult => HideAndSeekStep.GameExitNotice,
            _ => HideAndSeekStep.End,
        };
        Enter(next, tick);
        return next;
    }

    private void Enter(HideAndSeekStep step, long tick) {
        Step = step;
        StepTick = tick;
        if (step == HideAndSeekStep.TeamMatchResult) {
            // No catches yet (part 1): the hiders win while one of them is here.
            Winner = teams.ContainsValue(HideTeam) ? HideTeam : SeekTeam;
        }
    }

    // Chosen here: a third of the players seek (at least 1, and at least 1 hides when there are 2 or more). No
    // field's constants and no client code give the seekers' count.
    public static int SeekerCount(int players) => players <= 1 ? players : Math.Clamp(players / 3, 1, players - 1);

    // The split when the teams go to the game area: the players in a random order, the first SeekerCount seek.
    public void Split(IEnumerable<long> characterIds, Random random) {
        teams.Clear();
        long[] order = characterIds.ToArray();
        random.Shuffle(order);
        int seekers = SeekerCount(order.Length);
        for (int i = 0; i < order.Length; i++) {
            teams[order[i]] = i < seekers ? SeekTeam : HideTeam;
        }
    }

    public byte TeamOf(long characterId) => teams.GetValueOrDefault(characterId);

    // A player left: a team left empty ends the game: in MoveGameArea short of players (the script's only way out
    // there), while hiding or seeking with the result. Returns the step it moved to, or null.
    public HideAndSeekStep? Remove(long characterId, long tick) {
        if (!teams.Remove(characterId)) {
            return null;
        }
        bool empty = !teams.ContainsValue(HideTeam) || !teams.ContainsValue(SeekTeam);
        if (!empty) {
            return null;
        }
        switch (Step) {
            case HideAndSeekStep.MoveGameArea:
                Enter(HideAndSeekStep.ShortOfUser, tick);
                return Step;
            case HideAndSeekStep.BeInHidingTeams or HideAndSeekStep.LookingForATeams:
                Enter(HideAndSeekStep.TeamMatchResult, tick);
                return Step;
            default:
                return null;
        }
    }
}

// A Hide and Seek game in a field (FieldManager.HideAndSeek), started by the script's create_field_game HideAndSeek.
// The field's update moves it through the steps (HideAndSeekState) and sets each step's user value on the trigger
// that made the game, whose states wait for them. Entering a step does the server's part: MoveGameArea splits the
// teams, sends each player's team (HideAndSeek 0) and moves the hiders to the game area's portal; LookingForATeams
// moves the seekers there and gives them the seek skill set (5); TeamMatchResult takes it back (6) and opens the
// result dialog (8). Part 1 has no catches and gives no reward items: the hiders always win.
public sealed class HideAndSeekField {
    // Chosen here: the result's round is 1, the game has one round. The client hands it to the dialog's setData only.
    public const int ResultRound = 1;

    private readonly ILogger logger = Log.Logger.ForContext<HideAndSeekField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly int triggerId;
    private readonly HideAndSeekState state;

    public bool Ended {
        get {
            lock (mutex) return state.Step == HideAndSeekStep.End;
        }
    }

    public HideAndSeekStep Step {
        get {
            lock (mutex) return state.Step;
        }
    }

    public HideAndSeekField(FieldManager field, int triggerId) {
        this.field = field;
        this.triggerId = triggerId;
        state = new HideAndSeekState(field.FieldTick, logger);
        field.UserValues.Set(triggerId, nameof(HideAndSeekStep.WaitForEnterUser), 1);
        logger.Information("Hide and Seek game on {MapId} started by trigger {TriggerId}", field.MapId, triggerId);
    }

    public void Constant(string key, string value, string locale) {
        lock (mutex) state.SetConstant(key, value, locale, Maple2.Tools.Locale.Read());
    }

    public void Update(long tick) {
        lock (mutex) {
            HideAndSeekStep? next = state.Advance(tick, field.Players.Count);
            if (next != null) {
                Enter(next.Value);
            }
        }
    }

    public void Leave(FieldPlayer player) {
        lock (mutex) {
            HideAndSeekStep? next = state.Remove(player.Value.Character.Id, field.FieldTick);
            if (next != null) {
                Enter(next.Value);
            }
        }
    }

    // field_game_message: the banner to one team (custom 1 the hiders, 2 the seekers).
    public void Message(int team, string script, int duration) {
        lock (mutex) {
            foreach (FieldPlayer player in TeamPlayers((byte) team)) {
                player.Session.Send(MassiveEventPacket.Banner(BannerType.Text, script, duration));
            }
        }
    }

    private IEnumerable<FieldPlayer> TeamPlayers(byte team) {
        return field.Players.Values.Where(player => state.TeamOf(player.Value.Character.Id) == team).ToList();
    }

    private void Enter(HideAndSeekStep step) {
        switch (step) {
            case HideAndSeekStep.MoveGameArea:
                state.Split(field.Players.Values.Select(player => player.Value.Character.Id), Random.Shared);
                foreach (FieldPlayer player in field.Players.Values) {
                    byte team = state.TeamOf(player.Value.Character.Id);
                    field.Broadcast(HideAndSeekPacket.Info(player.ObjectId, team != 0, false, 0, 0, team));
                }
                MoveTeam(HideAndSeekState.HideTeam);
                break;
            case HideAndSeekStep.LookingForATeams:
                MoveTeam(HideAndSeekState.SeekTeam);
                foreach (FieldPlayer seeker in TeamPlayers(HideAndSeekState.SeekTeam)) {
                    seeker.Session.Send(HideAndSeekPacket.AddSkillSet(seeker.ObjectId, state.Int("SeekTeamSkillSetID")));
                }
                break;
            case HideAndSeekStep.TeamMatchResult:
                foreach (FieldPlayer seeker in TeamPlayers(HideAndSeekState.SeekTeam)) {
                    seeker.Session.Send(HideAndSeekPacket.RemoveSkillSet(seeker.ObjectId, state.Int("SeekTeamSkillSetID")));
                }
                // No reward items in part 1 (item 0, count 0: the dialog shows no reward row).
                foreach (FieldPlayer player in field.Players.Values.Where(player => state.TeamOf(player.Value.Character.Id) != 0)) {
                    player.Session.Send(HideAndSeekPacket.Result(player.ObjectId, ResultRound, 0, 0));
                }
                logger.Information("Hide and Seek game on {MapId}: team {Winner} won", field.MapId, state.Winner);
                break;
        }
        field.UserValues.Set(triggerId, step.ToString(), 1);
    }

    private void MoveTeam(byte team) {
        if (!field.TryGetPortal(state.Int("PortalGameAreaID"), out FieldPortal? portal)) {
            logger.Warning("Hide and Seek game on {MapId} has no game area portal {PortalId}", field.MapId, state.Int("PortalGameAreaID"));
            return;
        }
        foreach (FieldPlayer player in TeamPlayers(team)) {
            player.MoveToPortal(portal);
        }
    }
}
