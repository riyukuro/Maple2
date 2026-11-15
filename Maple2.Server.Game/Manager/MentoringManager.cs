using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.World.Service;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Util.Sync;
using System.Collections.Concurrent;
using Maple2.Server.Core.Sync;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class MentoringManager : IDisposable {
    private readonly GameSession session;
    public MentorRole Role => session.Player.Value.Character.MentorRole;


    private readonly ILogger logger = Log.Logger.ForContext<MentoringManager>();

    // The requests and pairs of this game server are checked and stored one at a time; the unique (mentor, mentee)
    // index keeps a second row out when two game servers store the same request.
    private static readonly object StoreLock = new();

    // One take of the mentor coins at a time (TakeMentorCoins): its count today is read before its store.
    private readonly object takeLock = new();

    private MentoringConfigTable Config => session.TableMetadata.MentoringConfigTable;

    // The running ReturnUser season. Without one the client's menu does not open the mentoring dialogs, and no
    // request or pair is stored or sent. Only a mentoring row is a season (value3 the season number, value2 10003):
    // the stamp ReturnUser rows have no value3 and run at the same time as some seasons (KR/NA 180112 in season 5's
    // 180110, CN 180017 in 180018, CN 180003 in 180001), so the first running row is not always the season.
    private ReturnUser? Season => SeasonEvent?.Metadata.Data as ReturnUser;
    private GameEvent? SeasonEvent => session.FindEvent(GameEventType.ReturnUser)
        .Where(gameEvent => gameEvent.Metadata.Data is ReturnUser { SeasonId: > 0 })
        .MinBy(gameEvent => gameEvent.Id);

    // Whether a season runs now (the returning-user party buff, PartyBuff.ReturnUser).
    public bool SeasonRuns => SeasonEvent != null;

    // The partners whose login and logout are pushed as Mentor 15 (the online flag last sent for each).
    // A listener stops once its partner's entry is removed or replaced.
    private sealed class PartnerState(bool online) {
        public volatile bool Online = online;
    }
    private readonly ConcurrentDictionary<long, PartnerState> partnerOnline = new();
    private readonly CancellationTokenSource tokenSource = new();

    public MentoringManager(GameSession session) {
        this.session = session;
    }

    public void Dispose() {
        if (tokenSource.IsCancellationRequested) {
            return;
        }
        tokenSource.Cancel();
        partnerOnline.Clear();
    }

    // The register button of the mentee page (Mentor 4, role Mentee) and of the mentor page (Mentor 8, role Mentor).
    // The client answers Mentor 1 by setting the role on the player with that object id, for every client that
    // shows the player, and Mentor 4 or 8 by closing the dialog with a notice.
    public void UpdateRole(MentorRole role) {
        if (role is not (MentorRole.Mentee or MentorRole.Mentor)) {
            return;
        }
        if (SeasonEvent is not { Metadata.Data: ReturnUser returnUser } seasonEvent) {
            // No season runs; the client's menu does not open the dialog then, so only a dialog left open from
            // the season gets here. The register box waits for Mentor 4 or 8, so each refusal sends a notice:
            // a mentee "not a returning user" (1), a mentor "invalid target" (3), as the client's error list
            // (CMentoringManager::OnError, 1 to 9) has no season text; its own season notice is local
            // (uigamemenudialog.cpp:2059).
            session.Send(MentorPacket.Error(role == MentorRole.Mentee ? MentorError.IsNotReturnUser : MentorError.InvalidTarget));
            return;
        }
        if (Role is MentorRole.Mentee or MentorRole.Mentor) {
            // A second press of the own page's button gets the register notice again (Mentor 4 or 8), which closes
            // the box that waits for it; nothing is stored again. The other page's button gets the client's
            // "invalid target" notice (chosen here: the client has no notice of its own for it).
            if (Role != role) {
                session.Send(MentorPacket.Error(MentorError.InvalidTarget));
            } else {
                session.Send(role == MentorRole.Mentee ? MentorPacket.AssignReturningUser() : MentorPacket.AssignMentor());
            }
            return;
        }
        // The client opens the mentee register page only for role 1 and the mentor register page only for role 4
        // (UIMentoringDialog::GetTabs, uimentoringrewardpage.cpp:115); the role is set at the season's login (Load).
        // A request from another role gets the same notices as the no-season refusal: a mentee "not a returning
        // user" (1, the text the client has for it), a mentor "invalid target" (3).
        if (Role != (role == MentorRole.Mentee ? MentorRole.MenteeCandidate : MentorRole.MentorCandidate)) {
            session.Send(MentorPacket.Error(role == MentorRole.Mentee ? MentorError.IsNotReturnUser : MentorError.InvalidTarget));
            return;
        }

        // The level the client's menu asks before it opens the register page (uigamemenudialog.cpp:2078-2090,
        // MentorRequireLevel and MenteeRequireLevel of constants.xml, KR 50 and 30). Its own notice is local and
        // the error list has no level text, so a lower level gets the season refusal's code.
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        int requiredLevel = role == MentorRole.Mentee ? constants.MenteeRequireLevel : constants.MentorRequireLevel;
        if (session.Player.Value.Character.Level < requiredLevel) {
            session.Send(MentorPacket.Error(role == MentorRole.Mentee ? MentorError.IsNotReturnUser : MentorError.InvalidTarget));
            return;
        }

        // One registered character per account for the season (MentoringRegistration): the account's other characters
        // hold role 3 or 6 from their login and are refused above; a registration of another character found here
        // (stored since this login) gets the same refusal.
        if (Registration(seasonEvent) is { } registered && registered.CharacterId != session.CharacterId) {
            session.Send(MentorPacket.Error(role == MentorRole.Mentee ? MentorError.IsNotReturnUser : MentorError.InvalidTarget));
            return;
        }
        // The account's registration is stored before the role is given: a registration lost with the session would
        // let another character of the account register as well. A store the database refuses gets the refusal's
        // notice and changes nothing.
        session.GameEvent.Get(GameEventUserValueType.MentoringRegistration, seasonEvent.Id, seasonEvent.EndTime);
        session.GameEvent.Set(seasonEvent.Id, GameEventUserValueType.MentoringRegistration, MentoringRegistration.Text(role, session.CharacterId));
        if (!session.GameEvent.Store(seasonEvent.Id, GameEventUserValueType.MentoringRegistration)) {
            logger.Warning("Mentoring registration of {CharacterId} not stored", session.CharacterId);
            session.GameEvent.Set(seasonEvent.Id, GameEventUserValueType.MentoringRegistration, "");
            session.Send(MentorPacket.Error(role == MentorRole.Mentee ? MentorError.IsNotReturnUser : MentorError.InvalidTarget));
            return;
        }

        // The role is set first: the season's quests can require it (require mentee, as 70600057 does).
        session.Player.Value.Character.MentorRole = role;
        if (role == MentorRole.Mentee) {
            foreach (int questId in returnUser.QuestIds) {
                if (session.Quest.Start(questId) == QuestError.s_quest_error_inventory_full) {
                    session.Send(QuestPacket.Error(QuestError.s_quest_error_inventory_full));
                }
            }
        }

        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            MentorRole = (int) role,
            Async = true,
        });
        ByteWriter update = MentorPacket.UpdateRole(role, session.Player.ObjectId);
        if (session.Field is null) {
            session.Send(update);
        } else {
            session.Field.Broadcast(update);
        }
        session.Send(role == MentorRole.Mentee ? MentorPacket.AssignReturningUser() : MentorPacket.AssignMentor());
        SendAccountCharacters();
    }

    // The account's registration of the running season, null when there is none.
    private (MentorRole Role, long CharacterId)? Registration(GameEvent season) {
        return MentoringRegistration.Parse(session.GameEvent.Peek(GameEventUserValueType.MentoringRegistration, season.Id));
    }

    // Mentor 12: the account's registered mentor and mentee characters of the running season (0 for none), which
    // the menu's role-6 and role-3 notices name.
    private void SendAccountCharacters() {
        (long mentor, long mentee) = SeasonEvent is { } season ? MentoringRegistration.Ids(Registration(season)) : (0, 0);
        session.Send(MentorPacket.AccountCharacters(mentor, mentee));
    }

    // The login: the waiting requests to a mentee (Mentor 2), the pairs (Mentor 3) and whether their other
    // characters are online (Mentor 15), then the account's registered characters (Mentor 12). Without a season only
    // the empty pair list and Mentor 12 with no one.
    public void Load() {
        LoadRole();
        if (Season == null) {
            session.Send(MentorPacket.MyList());
            SendAccountCharacters();
            return;
        }

        (MentorPacket.Entry[] requests, MentorPacket.Entry[] pairs) = Lists();
        session.Send(MentorPacket.MenteeInvitations(requests));
        session.Send(MentorPacket.MyList(pairs));
        session.Send(MentorPacket.PairOnline(pairs));
        ListenPartners(pairs);
        SendAttend();
        SendAccountCharacters();
    }

    // The role of the login, set before the field entry so the field's character block carries it (the client reads
    // the role only from there and from Mentor 1, and its menu opens the register pages only for roles 1 and 4,
    // UIMentoringDialog::GetTabs; a mentee is the party's returning user, uipartydialog.cpp:1080). While a ReturnUser
    // season runs it comes from the account's registration of the season (MentoringRegistration.Role): the registered
    // character gets its role (2 or 5), the account's others 3 (after a mentee) or 6 (after a mentor); while the
    // account has registered no one, a character is a mentee candidate (1) when it returns and a mentor candidate (4)
    // otherwise. Without a season every role goes back to none, so a registration ends with its season.
    // Returning, from the server data (GameEvent.xml): a mentoring ReturnUser row (value3 the season, value4 its
    // quests) gives the days away in value1 (KR/NA 30, CN 20); a date there would be read as "last online before
    // it" (only stamp rows, which are no season, have one). A ReturnUserCandidate row (KR 180105, CN 180004: value1 30,
    // value3 the next season, value4 that season's end; its note: players who return in its window are that
    // season's mentees too) keeps a return made while it runs for its season. A return is stored with its season,
    // so a later entry or a channel move keeps it. The days are a character's (its last save before this entry):
    // the data does not say account or character, and per character no returning account is left out. The rows'
    // levels are not read: the client's menu checks the constants' levels, as UpdateRole does.
    private void LoadRole() {
        MentorRole role = Role;
        MentorRole candidate = MentorRole.None;
        GameEvent? season = SeasonEvent;
        MoveUnseasoned(season);
        if (season?.Metadata.Data is ReturnUser returnUser) {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long lastSave = session.EntryLastSave;
            // A return already kept for this season (from a window, or from an earlier entry) is stored on the season's
            // row too. When the season was extended after a return was stored, the loaded value already follows the
            // new end (EventValueRules.FollowSeason, on each load); the database row keeps the end it was stored with.
            if (Returning(returnUser.DateInactiveSince, returnUser.DaysInactive, lastSave, now)
                || session.GameEvent.HasValue(GameEventUserValueType.MentoringReturnUser, returnUser.SeasonId.ToString(), now)) {
                StoreReturn(season.Id, season.EndTime, returnUser.SeasonId);
            }
            foreach (GameEvent window in session.FindEvent(GameEventType.ReturnUserCandidate)) {
                // MinLevel is value1, the days away (named a level by the ingest; the row's note and the matching
                // ReturnUser rows read it as days).
                if (window.Metadata.Data is ReturnUserCandidate next && Returning(DateTimeOffset.MinValue, next.MinLevel, lastSave, now)) {
                    long until = next.UnknownDate > DateTimeOffset.MinValue ? next.UnknownDate.ToUnixTimeSeconds() : window.EndTime;
                    StoreReturn(window.Id, Math.Max(until, window.EndTime), next.Season);
                }
            }
            bool returning = session.GameEvent.HasValue(GameEventUserValueType.MentoringReturnUser, returnUser.SeasonId.ToString(), now);
            candidate = MentoringRegistration.Role(Registration(season), session.CharacterId, returning);
        }
        if (candidate == role) {
            return;
        }
        session.Player.Value.Character.MentorRole = candidate;
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            MentorRole = (int) candidate,
            Async = true,
        });
    }

    // Mentoring stored before rows and registrations had a season (AddMentoringSeason): the character's requests
    // and pairs (season 0) and the account's registered character (event 0, which the migration made from a stored
    // role 2 or 5) go to the season running at the first login since, so an upgrade during a season keeps them. A
    // login without a season ends them instead: the season they were made in is over, and a later one starts
    // from nothing. A registration the account already has for the season is kept and the old one dropped.
    private void MoveUnseasoned(GameEvent? season) {
        int? seasonId = season?.Metadata.Data is ReturnUser returnUser ? returnUser.SeasonId : null;
        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.MoveUnseasonedMentoring(session.CharacterId, seasonId)) {
            logger.Warning("Mentoring rows of {CharacterId} not moved to season {Season}", session.CharacterId, seasonId);
        }
        GameEventUserValue? old = db.GetAccountEventValues(session.AccountId)
            .FirstOrDefault(value => value.EventId == 0 && value.Type == GameEventUserValueType.MentoringRegistration);
        if (old == null) {
            return;
        }
        if (season != null && Registration(season) == null && MentoringRegistration.Parse(old.Value) != null) {
            session.GameEvent.Get(GameEventUserValueType.MentoringRegistration, season.Id, season.EndTime);
            session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringRegistration, old.Value);
            if (!session.GameEvent.Store(season.Id, GameEventUserValueType.MentoringRegistration)) {
                // Kept for the next login.
                logger.Warning("Mentoring registration of account {AccountId} not moved to the season", session.AccountId);
                session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringRegistration, "");
                return;
            }
        }
        db.RemoveAccountEventValue(old, session.AccountId);
    }

    // The return for `seasonNumber`, kept on the event `eventId` until `until`; stored at once. A value that already
    // holds this season to `until` or later is not stored again; a value loaded past its stored end through
    // FollowSeason is such a value, so its row keeps the old end and is followed again at the next load.
    private void StoreReturn(int eventId, long until, int seasonNumber) {
        GameEventUserValue value = session.GameEvent.Get(GameEventUserValueType.MentoringReturnUser, eventId, until);
        string text = seasonNumber.ToString();
        if (value.Value == text && value.ExpirationTime >= until) {
            return;
        }
        if (value.Value != text) {
            session.GameEvent.Set(eventId, GameEventUserValueType.MentoringReturnUser, seasonNumber);
        }
        session.GameEvent.Extend(GameEventUserValueType.MentoringReturnUser, eventId, until);
        if (!session.GameEvent.Store(eventId, GameEventUserValueType.MentoringReturnUser)) {
            logger.Warning("Mentoring return of {CharacterId} not stored", session.CharacterId);
        }
    }

    // Whether a character last saved at `lastSave` (epoch seconds; 0 never saved) returns at `now`: its last save is
    // before `since`, or its days away reach `days`. Neither given: no return.
    public static bool Returning(DateTimeOffset since, int days, long lastSave, long now) {
        if (lastSave <= 0) {
            return false;
        }
        if (since > DateTimeOffset.MinValue && lastSave < since.ToUnixTimeSeconds()) {
            return true;
        }
        return days > 0 && now - lastSave >= days * 86400L;
    }

    // The mentee's attend streak and next claim time (Mentor 10), kept per season. Only a mentee has the reward
    // page (UIMentoringDialog::GetTabs, role 2).
    public void SendAttend() {
        GameEvent? season = SeasonEvent;
        if (season?.Metadata.Data is not ReturnUser || Role != MentorRole.Mentee) {
            return;
        }
        (int level, long next) = Attend(season);
        session.Send(MentorPacket.AttendDate(level, next));
    }

    // The reward page's button (Mentor 0): the mentee coin of the streak day, once per day. A claim before its time
    // only resends the date, which disables the button again. At the token cap nothing is given (Mentor 16, 9).
    public void ClaimAttendReward() {
        GameEvent? season = SeasonEvent;
        if (season?.Metadata.Data is not ReturnUser || Role != MentorRole.Mentee) {
            return;
        }
        (int level, long next) = Attend(season);
        DateTime now = DateTime.Now;
        if (!MentoringAttend.Available(next, now.ToEpochSeconds())) {
            session.Send(MentorPacket.AttendDate(level, next));
            return;
        }
        if (session.Currency[CurrencyType.MenteeToken] >= session.ServerTableMetadata.ConstantsTable.MenteeTokenMax
            || MenteeCoinsToday(season, now) >= Config.MaxMenteeDailyRewardCoin) {
            session.Send(MentorPacket.Error(MentorError.FullEarnCoin));
            return;
        }

        (int coin, int newLevel, long newNext) = MentoringAttend.Claim(Config, level, next, now);
        // The day (streak and next claim time), the coins today and the token are stored together (a purchase store,
        // as TakeMentorCoins): a lost session can neither give the day's coin twice nor use up the day without it.
        // A claim that is not stored gives nothing and keeps the day; the date sent again enables the button.
        long today = MenteeCoinsToday(season, now);
        long room = session.ServerTableMetadata.ConstantsTable.MenteeTokenMax - session.Currency[CurrencyType.MenteeToken];
        long given = Math.Max(0, Math.Min(Config.RewardableCoin(coin, today, mentee: true), room));
        GameEventUserValue? heldLevel = session.GameEvent.Held(season.Id, GameEventUserValueType.MentoringAttendLevel);
        GameEventUserValue? heldNext = session.GameEvent.Held(season.Id, GameEventUserValueType.MentoringAttendNext);
        GameEventUserValue? heldCount = session.GameEvent.Held(season.Id, GameEventUserValueType.MentoringMenteeCoinToday);
        if (heldLevel == null || heldNext == null || heldCount == null) {
            return;
        }
        var values = new List<GameEventUserValue> {
            Value(heldLevel, newLevel),
            Value(heldNext, newNext),
            Value(heldCount, today + given),
        };

        CurrencyManager.Added added = default;
        bool stored = session.TrySavePurchase(db => {
            added = session.Currency.AddUnsent(CurrencyType.MenteeToken, given);
            if (!db.SaveGameEventUserValues(session.CharacterId, values) || !db.SaveCurrency(session.Player)) {
                return false;
            }
            // The values reach memory once stored, under the account lock, so no session save between writes the old
            // ones.
            db.AfterCommit(() => {
                session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringAttendLevel, newLevel);
                session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringAttendNext, newNext);
                session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringMenteeCoinToday, today + given);
            });
            return true;
        }, () => session.Currency.TakeBackAdded(added));
        if (!stored) {
            logger.Warning("Mentoring attend of {CharacterId} not stored, no coin given", session.CharacterId);
            session.Send(MentorPacket.AttendDate(level, next));
            return;
        }
        session.Currency.NotifyAdded(added);
        if (given > 0) {
            SendDailyCoins();
        }
        session.Send(MentorPacket.AttendDate(newLevel, newNext));
    }

    // A held event value with a new value, its expiration kept, to store.
    private static GameEventUserValue Value(GameEventUserValue held, long value) {
        var stored = new GameEventUserValue(held.Type, held.ExpirationTime, held.EventId);
        stored.SetValue(value.ToString());
        return stored;
    }

    // The mentee coins of the mentoring rewards (the attend coin and a quest's menteeCoin). A mentee's coins in a
    // running season count against the table's daily cap (maxMenteeDailyRewardCoin), which the mentee page shows
    // with the coins today of Mentor 11: only the part under the cap is given. Anyone else (no season, a mentor, a
    // player without a role; the page shows them 0) gets the coin as before. The count holds only what the token
    // cap (MenteeTokenMax) lets in. It is stored at once; a store the database refuses is logged and the coin is
    // still given, as the claim it pays for is already used up (the attend day stored, the quest completed).
    // A quest's coin given to a paired mentee also earns its mentor the same coin (EarnMentorCoins).
    // Returns the coin given.
    public long AddMenteeCoin(long coin, bool fromQuest = true) {
        if (coin <= 0) {
            return 0;
        }
        GameEvent? season = SeasonEvent;
        if (season?.Metadata.Data is not ReturnUser || Role != MentorRole.Mentee) {
            session.Currency.Change(CurrencyType.MenteeToken, coin);
            return coin;
        }

        // One time for the day's value and its end, so a coin given across midnight counts to one day.
        DateTime now = DateTime.Now;
        long today = MenteeCoinsToday(season, now);
        long room = session.ServerTableMetadata.ConstantsTable.MenteeTokenMax - session.Currency[CurrencyType.MenteeToken];
        long given = Math.Min(Config.RewardableCoin(coin, today, mentee: true), room);
        if (given <= 0) {
            return 0;
        }
        session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringMenteeCoinToday, today + given);
        if (!session.GameEvent.Store(season.Id, GameEventUserValueType.MentoringMenteeCoinToday)) {
            logger.Warning("Mentee coins today of {CharacterId} not stored", session.CharacterId);
        }
        session.Currency.Change(CurrencyType.MenteeToken, given);
        SendDailyCoins();
        if (fromQuest) {
            EarnMentorCoins(season, given);
        }
        return given;
    }

    // Chosen here (no client code or table says how a mentor earns coins; the client has only the mentor's daily
    // cap, maxMentorDailyRewardCoin, and the mentor page's count of Mentor 11): the mentee coins a paired mentee gets
    // from a quest (the season missions' menteeCoin) earn its mentor the same coins. They are added to the pair's
    // row, which the mentor takes from (TakeMentorCoins), so a mentor on another channel or offline gets them too;
    // its game server is told at once.
    private void EarnMentorCoins(GameEvent season, long coins) {
        if (season.Metadata.Data is not ReturnUser { SeasonId: > 0 } returnUser) {
            return;
        }
        long mentorId;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            mentorId = db.AddMentorCoins(session.CharacterId, returnUser.SeasonId, coins);
        }
        if (mentorId == 0) {
            return;
        }
        // After a store this coin may be given in: its account lock is let go first.
        session.AfterStore(() => {
            try {
                session.World.Mentoring(new MentoringRequest {
                    ReceiverId = mentorId,
                    Coins = new MentoringRequest.Types.Coins(),
                });
            } catch (Exception ex) {
                logger.Warning(ex, "Mentor coins of {CharacterId} not announced to mentor {MentorId}", session.CharacterId, mentorId);
            }
        });
    }

    // The mentor coins the mentor's mentees earned for it (EarnMentorCoins), at each field entry and when a mentee
    // earns some: given as MentorToken up to the mentor's daily cap (maxMentorDailyRewardCoin, counted per day as the
    // mentee's coins are, MentoringAttend) and up to MentorTokenMax, the coins over either are dropped. The pairs'
    // coins, the count today and the token are stored together (a purchase store): a take that is not stored gives
    // nothing and leaves the coins on the pairs. A coin counts on the day the mentor takes it.
    public void TakeMentorCoins() {
        lock (takeLock) {
            if (Role != MentorRole.Mentor || SeasonEvent is not { Metadata.Data: ReturnUser { SeasonId: > 0 } returnUser } season) {
                return;
            }
            IList<(long Id, long Coins)> owed;
            using (GameStorage.Request db = session.GameStorage.Context()) {
                owed = db.GetMentorCoins(session.CharacterId, returnUser.SeasonId);
            }
            long total = owed.Sum(pair => pair.Coins);
            if (total <= 0) {
                return;
            }

            DateTime now = DateTime.Now;
            long today = MentorCoinsToday(season, now);
            long room = Math.Max(0, session.ServerTableMetadata.ConstantsTable.MentorTokenMax - session.Currency[CurrencyType.MentorToken]);
            long given = Math.Min(Config.RewardableCoin(total, today, mentee: false), room);
            GameEventUserValue? held = session.GameEvent.Held(season.Id, GameEventUserValueType.MentoringMentorCoinToday);
            if (held == null) {
                return;
            }
            var count = new GameEventUserValue(GameEventUserValueType.MentoringMentorCoinToday, held.ExpirationTime, season.Id);
            count.SetValue((today + given).ToString());

            CurrencyManager.Added added = default;
            bool stored = session.TrySavePurchase(db => {
                if (!db.TakeMentorCoins(owed)) {
                    return false;
                }
                added = session.Currency.AddUnsent(CurrencyType.MentorToken, given);
                if (!db.SaveGameEventUserValues(session.CharacterId, [count]) || !db.SaveCurrency(session.Player)) {
                    return false;
                }
                // The count reaches memory once stored, under the account lock, so no session save between writes the
                // old one.
                db.AfterCommit(() => session.GameEvent.Set(season.Id, GameEventUserValueType.MentoringMentorCoinToday, today + given));
                return true;
            }, () => session.Currency.TakeBackAdded(added));
            if (!stored) {
                logger.Warning("Mentor coins {Coins} of {CharacterId} not taken", total, session.CharacterId);
                return;
            }
            session.Currency.NotifyAdded(added);
            if (added.Amount < total) {
                logger.Information("Mentor {CharacterId} took {Given} of {Coins} mentor coins (daily cap or MentorTokenMax)", session.CharacterId, added.Amount, total);
            }
            SendDailyCoins();
        }
    }

    // The world's word that a mentee earned this mentor coins: taken on the field's thread, as the field entry does;
    // a mentor between fields takes them in the next field.
    public void ScheduleTakeMentorCoins() {
        if (session.Field is not { } field) {
            return;
        }
        field.Scheduler.Schedule(() => {
            if (session.Field == field) {
                TakeMentorCoins();
            }
        });
    }

    // Mentor 11: the coins today the mentoring page shows against the daily cap, until the next midnight. In a
    // running season a mentee gets its mentee coins today and a mentor its mentor coins today; anyone else 0.
    public void SendDailyCoins() {
        long midnight = DateTime.Now.Date.AddDays(1).ToEpochSeconds();
        GameEvent? season = SeasonEvent;
        if (season?.Metadata.Data is not ReturnUser || Role is not (MentorRole.Mentee or MentorRole.Mentor)) {
            session.Send(MentorPacket.DailyPoints(0, midnight));
            return;
        }
        long today = Role == MentorRole.Mentee ? MenteeCoinsToday(season, DateTime.Now) : MentorCoinsToday(season, DateTime.Now);
        session.Send(MentorPacket.DailyPoints((int) today, midnight));
    }

    // The day's value ends at the midnight after `now` and is tested against `now`.
    private long MenteeCoinsToday(GameEvent season, DateTime now) {
        long midnight = now.Date.AddDays(1).ToEpochSeconds();
        return session.GameEvent.Get(GameEventUserValueType.MentoringMenteeCoinToday, season.Id, Math.Min(midnight, season.EndTime), now.ToEpochSeconds()).Long();
    }

    // As MenteeCoinsToday, for the mentor's coins.
    private long MentorCoinsToday(GameEvent season, DateTime now) {
        long midnight = now.Date.AddDays(1).ToEpochSeconds();
        return session.GameEvent.Get(GameEventUserValueType.MentoringMentorCoinToday, season.Id, Math.Min(midnight, season.EndTime), now.ToEpochSeconds()).Long();
    }

    private (int Level, long Next) Attend(GameEvent season) {
        int level = session.GameEvent.Get(GameEventUserValueType.MentoringAttendLevel, season.Id, season.EndTime).Int();
        long next = session.GameEvent.Get(GameEventUserValueType.MentoringAttendNext, season.Id, season.EndTime).Long();
        return (level, next);
    }

    // The mentee page's list request (Mentor 2).
    public void SendRequests() {
        session.Send(Season == null ? MentorPacket.MenteeInvitations() : MentorPacket.MenteeInvitations(Lists().Requests));
    }

    // UIInviteMentoringDialog::InviteMentor (uiinvitementoringdialog.cpp:408): a mentor invites a mentee by name. The
    // mentor gets Mentor 5 with the mentee's name; the mentee, when online, Mentor 14 with the request.
    // The client removes a candidate row as soon as it sends (uiinvitementoringdialog.cpp:431), so every refusal
    // answers with a notice; a sender who is no mentor, or one without a season, gets "invalid target" (3).
    public void Invite(string name) {
        if (Role != MentorRole.Mentor) {
            session.Send(MentorPacket.Error(MentorError.InvalidTarget));
            return;
        }

        long targetId;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            targetId = db.GetCharacterId(name);
        }
        if (targetId == 0 || !session.PlayerInfo.GetOrFetch(targetId, out PlayerInfo? target)) {
            session.Send(MentorPacket.Error(MentorError.TargetNotExists));
            return;
        }
        if (targetId == session.CharacterId || target.AccountId == session.AccountId) {
            session.Send(MentorPacket.Error(MentorError.InvalidTarget));
            return;
        }
        if (target.MentorRole != MentorRole.Mentee) {
            session.Send(MentorPacket.Error(MentorError.TargetIsNotMentee));
            return;
        }
        if (Season is not { } season) {
            session.Send(MentorPacket.Error(MentorError.InvalidTarget));
            return;
        }

        // The limits are the server table's (constants.xml); the client does not read them.
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        MentorError? error;
        MentoringEntry? request;
        lock (StoreLock) {
            using GameStorage.Request db = session.GameStorage.Context();
            error = db.AddMentoringRequest(session.CharacterId, targetId, season.SeasonId, Constant.MaxMentees, constants.MentorMaxWaitingCount,
                constants.MenteeMaxReceivedCount, out request);
        }
        if (error != null || request == null) {
            session.Send(MentorPacket.Error(error ?? MentorError.TargetNotExists));
            return;
        }

        session.Send(MentorPacket.MenteeInvite(targetId, target.Name));
        try {
            session.World.Mentoring(new MentoringRequest {
                ReceiverId = targetId,
                Requested = new MentoringRequest.Types.Requested { RequestId = request.Id },
            });
        } catch (Exception ex) {
            logger.Warning(ex, "Mentoring request {Id} not delivered", request.Id);
        }
    }

    // A mentor's request stored for this player (from the world): shown when it still waits for this player.
    public void ReceiveRequest(long id) {
        if (Season is not { } season) {
            return;
        }

        MentoringEntry? row;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            row = db.GetMentoringEntry(id, season.SeasonId);
        }
        if (row is not { Accepted: false } || row.MenteeId != session.CharacterId || ToEntry(row) is not { } request) {
            return;
        }
        session.Send(MentorPacket.ReceiveMenteeInvite(request));
    }

    // The mentee accepts a mentor's request (uiinvitementoringdialog.cpp:453): only a waiting request to this player.
    // The mentee gets Mentor 6 (the client moves the request to its pair and drops the others, which are removed
    // here too); the mentor, when online, its pair list again.
    public void Accept(long id) {
        if (Season is not { } season) {
            session.Send(MentorPacket.Error(MentorError.TargetNotExists));
            return;
        }

        MentorError? error;
        MentoringEntry? pair;
        lock (StoreLock) {
            using GameStorage.Request db = session.GameStorage.Context();
            error = db.AcceptMentoring(id, session.CharacterId, season.SeasonId, Constant.MaxMentees, out pair);
        }
        if (error != null || pair == null) {
            session.Send(MentorPacket.Error(error ?? MentorError.TargetNotExists));
            if (error is MentorError.InvalidTarget or MentorError.MenteeHasAlreadyMentor) {
                // The mentor has all its mentees, or this player has a mentor already: the request was removed,
                // the list shows the ones left.
                session.Send(MentorPacket.MenteeInvitations(Lists().Requests));
            }
            return;
        }

        session.Send(MentorPacket.AcceptMentorInvite(id));
        MailMentorReward(pair.MentorId);
        try {
            session.World.Mentoring(new MentoringRequest {
                ReceiverId = pair.MentorId,
                Reload = new MentoringRequest.Types.Reload(),
            });
        } catch (Exception ex) {
            logger.Warning(ex, "Mentoring pair {Id} not sent to the mentor", pair.Id);
        }
    }

    // The new mentor's reward for a pair: the mail of s_mentoring_new_mentor_mail_* (stringcommon.xml: the mentee's
    // name as {0} of the title and the content, the points as {1}) with the table's MentorReward coin as mentor points
    // (item 90000023, MentorToken when collected). The client parses the coin (mentoringdatatable.cpp:80) and reads it
    // nowhere, and no client code sends the mail, so the server sends it when a mentee accepts. Chosen here: the coin
    // is not counted against the mentor's daily cap (maxMentorDailyRewardCoin 400 is below the 1000 the mail names);
    // a table coin of 0 sends no mail; a mail or item the database refuses is logged and the pair stays.
    private void MailMentorReward(long mentorId) {
        int rewardCoin = Config.MentorRewardCoin;
        if (rewardCoin <= 0) {
            return;
        }
        Item? points = session.Field?.ItemDrop.CreateItem(MentoringAttend.MentorPointItemId, amount: rewardCoin);
        if (points == null) {
            logger.Error("Mentor reward item {ItemId} not created for {CharacterId}", MentoringAttend.MentorPointItemId, mentorId);
            return;
        }

        var mail = new Mail {
            ReceiverId = mentorId,
            SenderId = mentorId,
            Type = MailType.System,
            TitleArgs = [("str", session.PlayerName)],
            ContentArgs = [("str", session.PlayerName), ("str", rewardCoin.ToString())],
        };
        mail.SetSenderName(StringCode.s_mentoring_new_mentor_mail_sender);
        mail.SetTitle(StringCode.s_mentoring_new_mentor_mail_title);
        mail.SetContent(StringCode.s_mentoring_new_mentor_mail_content);

        using (GameStorage.Request db = session.GameStorage.Context()) {
            Mail? stored = db.CreateMail(mail);
            if (stored == null) {
                logger.Error("Mentor reward mail not created for {CharacterId}", mentorId);
                return;
            }
            points.Slot = -1;
            if (db.CreateItem(stored.Id, points) == null) {
                logger.Error("Mentor reward item not stored on mail {MailId}", stored.Id);
                db.DeleteMail(stored.Id, mentorId);
                return;
            }
            mail = stored;
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = mentorId,
                MailId = mail.Id,
            });
        } catch (Exception ex) {
            logger.Warning(ex, "Mentor reward mail {MailId} not announced", mail.Id);
        }
    }

    // The mentee declines a mentor's request (uiinvitementoringdialog.cpp:266): only a waiting request to this player.
    public void Decline(long id) {
        bool removed = false;
        if (Season is { } season) {
            lock (StoreLock) {
                using GameStorage.Request db = session.GameStorage.Context();
                removed = db.DeclineMentoring(id, session.CharacterId, season.SeasonId);
            }
        }
        session.Send(removed ? MentorPacket.DeclineMentorInvite(id) : MentorPacket.Error(MentorError.TargetNotExists));
    }

    // The stored pairs changed (from the world): the pair list again.
    public void Reload() {
        if (Season == null) {
            return;
        }

        MentorPacket.Entry[] pairs = Lists().Pairs;
        session.Send(MentorPacket.MyList(pairs));
        session.Send(MentorPacket.PairOnline(pairs));
        ListenPartners(pairs);
    }

    // A partner's login or logout (its channel changes) is sent as Mentor 15 with that partner alone, while the
    // pair is listed. A partner no longer in the list stops its listener at its next update.
    private void ListenPartners(MentorPacket.Entry[] pairs) {
        if (tokenSource.IsCancellationRequested) {
            return;
        }
        HashSet<long> current = pairs.Select(pair => pair.Partner.CharacterId).ToHashSet();
        foreach (long id in partnerOnline.Keys.Where(id => !current.Contains(id)).ToArray()) {
            partnerOnline.TryRemove(id, out _);
        }

        CancellationToken token = tokenSource.Token;
        foreach (MentorPacket.Entry pair in pairs) {
            long partnerId = pair.Partner.CharacterId;
            if (partnerOnline.TryGetValue(partnerId, out PartnerState? known)) {
                known.Online = pair.Partner.Online;
                continue;
            }
            var state = new PartnerState(pair.Partner.Online);
            if (!partnerOnline.TryAdd(partnerId, state)) {
                continue;
            }
            session.PlayerInfo.Listen(partnerId, new PlayerInfoListener(UpdateField.Channel, (_, info) => {
                if (token.IsCancellationRequested || !partnerOnline.TryGetValue(partnerId, out PartnerState? now) || now != state) {
                    return true;
                }
                bool online = info.Channel >= 0;
                if (online != state.Online) {
                    state.Online = online;
                    session.Send(MentorPacket.PairOnline((partnerId, online)));
                }
                return false;
            }));
        }
    }

    // The running season's waiting requests to this player (as mentee) and its pairs, each with the other character;
    // none without a season. A row of another season is not read: the client's accept box names the season's end as
    // the pair's end (uiinvitementoringdialog.cpp:226-253).
    private (MentorPacket.Entry[] Requests, MentorPacket.Entry[] Pairs) Lists() {
        if (Season is not { } season) {
            return ([], []);
        }
        IList<MentoringEntry> rows;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            rows = db.GetMentoring(session.CharacterId, season.SeasonId);
        }

        MentorPacket.Entry[] requests = rows.Where(row => !row.Accepted && row.MenteeId == session.CharacterId)
            .Select(ToEntry).OfType<MentorPacket.Entry>().ToArray();
        MentorPacket.Entry[] pairs = rows.Where(row => row.Accepted).Select(ToEntry).OfType<MentorPacket.Entry>().ToArray();
        return (requests, pairs);
    }

    private MentorPacket.Entry? ToEntry(MentoringEntry row) {
        return session.PlayerInfo.GetOrFetch(row.PartnerOf(session.CharacterId), out PlayerInfo? partner)
            ? new MentorPacket.Entry(row.Id, partner)
            : null;
    }
}
