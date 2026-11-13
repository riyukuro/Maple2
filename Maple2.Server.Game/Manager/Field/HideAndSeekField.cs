using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
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
    // Chosen here: a seeker's lives (the life bar of mode 2 type 2). Seekers lose one for a seek-skill shot that hits
    // no hider after the invincible time (61000023_main's InvincibleMessage, MAIN__6, tells the seekers so); no field
    // constant and no client code gives the count.
    public const int SeekerLives = 3;

    // Every key the hide-and-seek fields give (61000023-61000029). The ones the game does not use (exp, the watch
    // camera, the events) are kept for their own work.
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
    // The seekers' lives left, their catches, and each hider's object (the ride's health and the prop it hides).
    private readonly Dictionary<long, int> lives = [];
    private readonly Dictionary<long, int> kills = [];
    private readonly Dictionary<long, Hider> hiders = [];

    public sealed class Hider(int rideId, int prop, long health) {
        public int RideId { get; } = rideId;
        public int Prop { get; } = prop;
        public long MaxHealth { get; } = health;
        public long Health { get; set; } = health;
        public bool Caught => Health <= 0;
    }

    // A seeker's hit on a hider: the object's health after it, and whether it caught the hider.
    public readonly record struct HitResult(long Health, long MaxHealth, bool Caught);

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
            // The hiders win while one of them is here and not caught.
            Winner = HidersLeft ? HideTeam : SeekTeam;
        }
    }

    private bool HidersLeft => teams.Any(entry => entry.Value == HideTeam && !IsCaught(entry.Key));
    private bool SeekersLeft => teams.Any(entry => entry.Value == SeekTeam && lives.GetValueOrDefault(entry.Key) > 0);

    // The seekers look until every hider is caught or no seeker has a life left.
    private bool CheckOver(long tick) {
        if (Step != HideAndSeekStep.LookingForATeams || (HidersLeft && SeekersLeft)) {
            return false;
        }
        Enter(HideAndSeekStep.TeamMatchResult, tick);
        return true;
    }

    public bool IsCaught(long characterId) => hiders.TryGetValue(characterId, out Hider? hider) && hider.Caught;
    public int Lives(long characterId) => lives.GetValueOrDefault(characterId);
    public int Kills(long characterId) => kills.GetValueOrDefault(characterId);
    public Hider? HiderOf(long characterId) => hiders.GetValueOrDefault(characterId);
    public IEnumerable<int> HiddenProps => hiders.Values.Select(hider => hider.Prop).Where(prop => prop != 0);

    // Out of the game but in the field: a caught hider, a seeker without lives, or a player who came after the split.
    public bool Watching(long characterId) {
        return Step is >= HideAndSeekStep.MoveGameArea and <= HideAndSeekStep.LookingForATeams
               && (!teams.ContainsKey(characterId) || IsCaught(characterId) || (TeamOf(characterId) == SeekTeam && Step == HideAndSeekStep.LookingForATeams && Lives(characterId) <= 0));
    }

    // Chosen here: the ride a hider turns into is a random one of the field's rides, first among those with a prop of
    // their model on the map that no hider hides yet; that prop is hidden (mode 1) so the hider can take its place.
    // Neither the client nor the data says which props the server hides. Returns the ride and the prop (0: none).
    public (int RideId, int Prop) PickRide(HideAndSeekTable.Field field, Random random) {
        var used = new HashSet<int>(HiddenProps);
        var free = new List<(int, int)>();
        foreach (int rideId in field.Rides) {
            if (field.Props.TryGetValue(rideId, out IReadOnlyList<int>? props)) {
                free.AddRange(props.Where(prop => !used.Contains(prop)).Select(prop => (rideId, prop)));
            }
        }
        if (free.Count > 0) {
            return free[random.Next(free.Count)];
        }
        return field.Rides.Count == 0 ? (0, 0) : (field.Rides[random.Next(field.Rides.Count)], 0);
    }

    public void Hide(long characterId, int rideId, int prop, long health) {
        hiders[characterId] = new Hider(rideId, prop, Math.Max(1, health));
    }

    // Whether the seeker's hostile skills reach the player: a seeker with lives on a hider not caught, while seeking.
    public bool Hostile(long seekerId, long hiderId) {
        return Step == HideAndSeekStep.LookingForATeams && TeamOf(seekerId) == SeekTeam && Lives(seekerId) > 0
               && TeamOf(hiderId) == HideTeam && hiders.TryGetValue(hiderId, out Hider? hider) && !hider.Caught;
    }

    // A seek-skill hit: the hider's object loses the damage; at 0 the hider is caught and the seeker's catches grow. The
    // last catch ends the seeking (TeamMatchResult). Null when the hit does not count.
    public HitResult? Hit(long seekerId, long hiderId, long damage, long tick) {
        if (!Hostile(seekerId, hiderId) || damage <= 0) {
            return null;
        }
        Hider hider = hiders[hiderId];
        hider.Health = Math.Max(0, hider.Health - damage);
        if (hider.Caught) {
            kills[seekerId] = Kills(seekerId) + 1;
        }
        var result = new HitResult(hider.Health, hider.MaxHealth, hider.Caught);
        CheckOver(tick);
        return result;
    }

    // A seek-skill shot that hit no hider (it hit the map or a prop): the seeker loses a life, unless the seekers are
    // still invincible (SeekTeamInvincibleTick from the start of LookingForATeams). The last life of the last seeker ends
    // the seeking. Returns the lives left, or null when nothing was lost.
    public int? Miss(long seekerId, long tick) {
        if (Step != HideAndSeekStep.LookingForATeams || TeamOf(seekerId) != SeekTeam || Lives(seekerId) <= 0
            || tick - StepTick < Int("SeekTeamInvincibleTick")) {
            return null;
        }
        int left = lives[seekerId] = Lives(seekerId) - 1;
        CheckOver(tick);
        return left;
    }

    // Chosen here: a third of the players seek (at least 1, and at least 1 hides when there are 2 or more). No
    // field's constants and no client code give the seekers' count.
    public static int SeekerCount(int players) => players <= 1 ? players : Math.Clamp(players / 3, 1, players - 1);

    // The split when the teams go to the game area: the players in a random order, the first SeekerCount seek.
    public void Split(IEnumerable<long> characterIds, Random random) {
        teams.Clear();
        lives.Clear();
        kills.Clear();
        hiders.Clear();
        long[] order = characterIds.ToArray();
        random.Shuffle(order);
        int seekers = SeekerCount(order.Length);
        for (int i = 0; i < order.Length; i++) {
            teams[order[i]] = i < seekers ? SeekTeam : HideTeam;
            if (i < seekers) {
                lives[order[i]] = SeekerLives;
            }
        }
    }

    public byte TeamOf(long characterId) => teams.GetValueOrDefault(characterId);

    // A player left: a team left empty ends the game: in MoveGameArea short of players (the script's only way out
    // there), while hiding or seeking with the result. Returns the step it moved to, or null.
    public HideAndSeekStep? Remove(long characterId, long tick) {
        if (!teams.Remove(characterId)) {
            return null;
        }
        hiders.Remove(characterId);
        lives.Remove(characterId);
        if (CheckOver(tick)) {
            return Step;
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
// teams, turns each hider into one of the map's riding objects (hideAndSeek.xml) with its life bar (HideAndSeek 2),
// hides the props they replace (1), sends each player's team (0) and moves the hiders to the game area's portal;
// LookingForATeams moves the seekers there, gives them the seek skill set (5) and their lives (2 type 2). A seek-skill
// hit takes a hider's object health (4); at 0 the hider is caught: off the object, to the fail area and the watch
// dialog (7). A seek shot that lands away from the hiders costs the seeker a life after the invincible time.
// TeamMatchResult takes everything back (6, 3, 7 closed), gives the team rewards and opens the result dialog (8).
public sealed class HideAndSeekField {
    // Chosen here: the result's round is 1, the game has one round. The client hands it to the dialog's setData only.
    public const int ResultRound = 1;
    // The seek team's skill set (SeekTeamSkillSetID 99930041) holds this one skill (table/skillset.xml), level 1: a
    // const damage arrow (2500) whose landing splash (90001160) reaches 200 around it.
    public const int SeekSkillId = 90001159;
    public const float SeekSplashRadius = 200f;
    // Mode 2's bar types: 2 is the seekers' (client: the hud's slot mode). Chosen here: 1 for the hiders' object bar;
    // the client treats every type but 2 alike (the hud hidden, the bar at x 0).
    public const short SeekerBarType = 2;
    public const short HiderBarType = 1;

    private readonly ILogger logger = Log.Logger.ForContext<HideAndSeekField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly int triggerId;
    private readonly HideAndSeekState state;
    private readonly HideAndSeekTable.Field? table;
    // The seekers holding the seek skill set (mode 5 sent, no mode 6 yet).
    private readonly HashSet<long> skillSet = [];

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
        if (!field.ServerTableMetadata.HideAndSeekTable.Entries.TryGetValue(field.MapId, out table)) {
            logger.Warning("Hide and Seek game on {MapId}: the map has no hideAndSeek.xml row, the hiders stay players", field.MapId);
        }
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
            // A player leaving (portal, exit, channel change, logout) gives back what the game gave: the seek skill
            // set (the client keeps the set's quick-slot layout locked until a mode 6), the object ride, the life bar
            // and the watch dialog.
            Release(player);
            HideAndSeekStep before = state.Step;
            HideAndSeekStep? next = state.Remove(player.Value.Character.Id, field.FieldTick);
            if (next != null && next != before) {
                Enter(next.Value);
            }
        }
    }

    // A new game replaces this one (create_field_game reset): the players still here get back what it gave.
    public void Close() {
        lock (mutex) {
            foreach (FieldPlayer player in field.Players.Values) {
                Release(player);
            }
        }
    }

    // A player came into the field. After the split it is out of the game: it gets the players' game info, the hidden
    // props and, while the game runs, the watch dialog. After End there is nothing to watch: it goes back to its
    // return field, as the script's End state sends everyone (move_user 0).
    public void Arrive(FieldPlayer player) {
        lock (mutex) {
            if (state.Step == HideAndSeekStep.End) {
                long characterId = player.Value.Character.Id;
                field.Scheduler.Schedule(() => {
                    if (field.TryGetPlayerById(characterId, out FieldPlayer? late)) {
                        late.Session.ReturnField();
                    }
                });
                return;
            }
            if (state.Step < HideAndSeekStep.MoveGameArea) {
                return;
            }
            foreach (FieldPlayer other in field.Players.Values) {
                if (other != player) {
                    player.Session.Send(InfoOf(other));
                }
            }
            player.Session.Send(HidePropsPacket());
            if (state.Watching(player.Value.Character.Id)) {
                Watch(player);
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

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
    }

    // A seeker's hit on a hider (FieldPlayer.ApplyDamage): the player itself takes no damage. A seek-skill hit takes
    // its const damage from the hider's object; any other skill does nothing. False when the hit is not the game's.
    public bool Hit(FieldPlayer seeker, FieldPlayer hider, DamageRecord damage, SkillMetadataAttack attack) {
        lock (mutex) {
            long seekerId = seeker.Value.Character.Id;
            long hiderId = hider.Value.Character.Id;
            if (!state.Hostile(seekerId, hiderId)) {
                return false;
            }
            if (damage.SkillId != SeekSkillId) {
                return true;
            }
            long amount = attack.Damage.IsConstDamage ? attack.Damage.Value : 0;
            var target = new DamageRecordTarget(hider) {
                Position = seeker.Position,
                Direction = seeker.Transform.FrontAxis,
            };
            target.AddDamage(DamageType.Normal, amount);
            damage.Targets.TryAdd(hider.ObjectId, target);

            HideAndSeekStep before = state.Step;
            HideAndSeekState.HitResult? result = state.Hit(seekerId, hiderId, amount, field.FieldTick);
            if (result == null) {
                return true;
            }
            hider.Session.Send(HideAndSeekPacket.Life(hider.ObjectId, Hits(result.Value.Health), Hits(result.Value.MaxHealth)));
            if (result.Value.Caught) {
                Catch(seeker, hider);
            }
            if (state.Step != before) {
                Enter(state.Step);
            }
            return true;
        }
    }

    // A seek-skill shot landed (Skill CubeMagicPath at its position). Chosen here: it hit no hider when no hider's
    // object is within the landing splash's radius; the client sends no hit for the props it strikes, and the
    // invincible message (MAIN__6) only says that attacking a non-hider object costs a life.
    public void Landed(FieldPlayer seeker, Vector3 position) {
        lock (mutex) {
            bool near = field.Players.Values.Any(player => state.TeamOf(player.Value.Character.Id) == HideAndSeekState.HideTeam
                                                           && !state.IsCaught(player.Value.Character.Id)
                                                           && Vector3.Distance(player.Position, position) <= SeekSplashRadius);
            if (near) {
                return;
            }
            HideAndSeekStep before = state.Step;
            int? left = state.Miss(seeker.Value.Character.Id, field.FieldTick);
            if (left == null) {
                return;
            }
            seeker.Session.Send(HideAndSeekPacket.Life(seeker.ObjectId, left.Value, HideAndSeekState.SeekerLives));
            if (left.Value <= 0) {
                // Out of lives: the seeker loses the seek skills and its bar and watches.
                Release(seeker);
                Watch(seeker);
            }
            if (state.Step != before) {
                Enter(state.Step);
            }
        }
    }

    private void Catch(FieldPlayer seeker, FieldPlayer hider) {
        hider.Session.Ride.Dismount(RideOffType.HideAndSeek);
        hider.Session.Send(HideAndSeekPacket.End(hider.ObjectId));
        field.Broadcast(InfoOf(hider));
        field.Broadcast(InfoOf(seeker));
        if (field.TryGetPortal(state.Int("PortalFailAreaID"), out FieldPortal? portal)) {
            hider.MoveToPortal(portal);
        }
        Watch(hider);
        logger.Information("Hide and Seek game on {MapId}: {Seeker} caught {Hider}", field.MapId, seeker.Value.Character.Name, hider.Value.Character.Name);
    }

    // Chosen here: the watch dialog's int is WatchCameraID (the client keeps it and never reads it; the dialog finds
    // the cameras itself) and it opens unfolded.
    private void Watch(FieldPlayer player) {
        player.Session.Send(HideAndSeekPacket.Watch(player.ObjectId, state.Int("WatchCameraID"), true, false));
    }

    // Takes back what the game gave a player: the seek skill set, the object ride, the life bar and the watch dialog.
    private void Release(FieldPlayer player) {
        long characterId = player.Value.Character.Id;
        if (skillSet.Remove(characterId)) {
            player.Session.Send(HideAndSeekPacket.RemoveSkillSet(player.ObjectId, state.Int("SeekTeamSkillSetID")));
        }
        if (state.HiderOf(characterId) is { Caught: false } && player.Session.Ride.RideType == RideOnType.Object) {
            player.Session.Ride.Dismount(RideOffType.HideAndSeek);
        }
        if (state.TeamOf(characterId) != 0) {
            player.Session.Send(HideAndSeekPacket.End(player.ObjectId));
        }
        player.Session.Send(HideAndSeekPacket.Watch(player.ObjectId, 0, false, false));
    }

    private ByteWriter InfoOf(FieldPlayer player) {
        long characterId = player.Value.Character.Id;
        byte team = state.TeamOf(characterId);
        return HideAndSeekPacket.Info(player.ObjectId, team != 0, state.IsCaught(characterId), 0, state.Kills(characterId), team);
    }

    private ByteWriter HidePropsPacket() {
        // The client reads one byte per prop; the KR maps' search indexes all fit.
        return HideAndSeekPacket.HideProps(state.HiddenProps.Where(prop => prop is > 0 and <= byte.MaxValue).Select(prop => (byte) prop).ToList());
    }

    // An object's health in seek-skill hits (the bar counts hits: 5000 health under 2500 a hit is 2).
    private int Hits(long health) {
        long perHit = SeekDamage();
        return (int) Math.Min(int.MaxValue, perHit <= 0 ? health : (health + perHit - 1) / perHit);
    }

    private long SeekDamage() {
        if (!field.SkillMetadata.TryGet(SeekSkillId, 1, out SkillMetadata? skill)) {
            return 0;
        }
        return skill.Data.Motions.SelectMany(motion => motion.Attacks)
            .Where(attack => attack.Damage.IsConstDamage)
            .Select(attack => attack.Damage.Value)
            .FirstOrDefault();
    }

    private IEnumerable<FieldPlayer> TeamPlayers(byte team) {
        return field.Players.Values.Where(player => state.TeamOf(player.Value.Character.Id) == team).ToList();
    }

    // A hider turns into a riding object of the map (the riding table's object rides of hideAndSeek.xml).
    private void TurnIntoObject(FieldPlayer player) {
        if (table == null) {
            return;
        }
        (int rideId, int prop) = state.PickRide(table, Random.Shared);
        if (!field.RideMetadata.TryGet(rideId, out RideMetadata? ride)) {
            logger.Warning("Hide and Seek game on {MapId}: no ride {RideId}", field.MapId, rideId);
            return;
        }
        if (!player.Session.Ride.MountObject(ride)) {
            return;
        }
        state.Hide(player.Value.Character.Id, rideId, prop, ride.Stats.GetValueOrDefault(BasicAttribute.Health));
        HideAndSeekState.Hider hider = state.HiderOf(player.Value.Character.Id)!;
        int hits = Hits(hider.MaxHealth);
        // Chosen here: the bar's first value (the swf's "init") is the full count, as the arcades' bars start full.
        player.Session.Send(HideAndSeekPacket.LifeBar(player.ObjectId, HiderBarType, hits, hits, hits));
    }

    private void Enter(HideAndSeekStep step) {
        switch (step) {
            case HideAndSeekStep.MoveGameArea:
                state.Split(field.Players.Values.Select(player => player.Value.Character.Id), Random.Shared);
                foreach (FieldPlayer hider in TeamPlayers(HideAndSeekState.HideTeam)) {
                    TurnIntoObject(hider);
                }
                foreach (FieldPlayer player in field.Players.Values) {
                    field.Broadcast(InfoOf(player));
                }
                field.Broadcast(HidePropsPacket());
                MoveTeam(HideAndSeekState.HideTeam);
                break;
            case HideAndSeekStep.LookingForATeams:
                MoveTeam(HideAndSeekState.SeekTeam);
                string? message = state.Constant("InvincibleMessage");
                foreach (FieldPlayer seeker in TeamPlayers(HideAndSeekState.SeekTeam)) {
                    skillSet.Add(seeker.Value.Character.Id);
                    seeker.Session.Send(HideAndSeekPacket.AddSkillSet(seeker.ObjectId, state.Int("SeekTeamSkillSetID")));
                    int lives = state.Lives(seeker.Value.Character.Id);
                    seeker.Session.Send(HideAndSeekPacket.LifeBar(seeker.ObjectId, SeekerBarType, lives, lives, HideAndSeekState.SeekerLives));
                    // The invincible time's message (MAIN__6: the seekers are invincible and lose no lives yet),
                    // shown like the script's field_game_message banners for InvincibleNoticeTick.
                    if (!string.IsNullOrEmpty(message) && state.Int("SeekTeamInvincibleTick") > 0) {
                        seeker.Session.Send(MassiveEventPacket.Banner(BannerType.Text, message, state.Int("InvincibleNoticeTick")));
                    }
                }
                break;
            case HideAndSeekStep.TeamMatchResult:
                foreach (FieldPlayer player in field.Players.Values) {
                    Release(player);
                }
                foreach (FieldPlayer player in field.Players.Values.Where(player => state.TeamOf(player.Value.Character.Id) != 0)) {
                    (int itemId, int count) = Reward(player);
                    player.Session.Send(HideAndSeekPacket.Result(player.ObjectId, ResultRound, itemId, count));
                }
                logger.Information("Hide and Seek game on {MapId}: team {Winner} won", field.MapId, state.Winner);
                break;
        }
        field.UserValues.Set(triggerId, step.ToString(), 1);
    }

    // The team's reward (WinnerRewardItem* or LoserRewardItem* of the server's region): the item goes to the bag, or
    // the mail when the bag is full. The result dialog shows it only once it is given.
    private (int ItemId, int Count) Reward(FieldPlayer player) {
        string prefix = state.TeamOf(player.Value.Character.Id) == state.Winner ? "Winner" : "Loser";
        int itemId = state.Int(prefix + "RewardItemID");
        int count = state.Int(prefix + "RewardItemCount");
        if (itemId == 0 || count <= 0) {
            return (0, 0);
        }
        Item? item = field.ItemDrop.CreateItem(itemId, state.Int(prefix + "RewardItemRank"), count);
        if (item == null) {
            logger.Error("Hide and Seek game on {MapId}: reward item {ItemId} could not be made", field.MapId, itemId);
            return (0, 0);
        }
        if (!player.Session.Item.AddOrMail(item, true)) {
            logger.Error("Hide and Seek game on {MapId}: reward item {ItemId} went to neither bag nor mail for {Player}", field.MapId, itemId, player.Value.Character.Name);
            return (0, 0);
        }
        return (itemId, count);
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
