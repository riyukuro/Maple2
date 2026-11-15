using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class CurrencyManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private Currency Currency => session.Player.Value.Currency;
    // The wallets are changed by the session's packet thread and by other threads (the field's: arcade, dark stream,
    // mini game, rank duel, trigger and dungeon rewards, items given there; another session's: a trade's hand-over, a
    // wedding hall paid by the partner): each change reads and writes its balance under this lock, so one cannot undo
    // another. The lock is a leaf: only the balance and its packet are under it; condition and quest updates (which may
    // take the bag lock) run after it is let go.
    private readonly object walletLock = new();
    // Meso packets sent so far (under walletLock): a held take's refusal tells by it whether a packet showed the take.
    private long mesoSends;
    private readonly ILogger logger = Log.Logger.ForContext<CurrencyManager>();

    public CurrencyManager(GameSession session) {
        this.session = session;
    }

    public long Meret {
        get => Currency.Meret;
        set => SetMeret(value, reportSpent: true);
    }

    // reportSpent false: the caller reports the spend itself (ReportMeretSpent), once what was bought is kept.
    public void SetMeret(long value, bool reportSpent) {
        if (value < 0) {
            throw new ArgumentException("Not enough Merets");
        }

        long delta;
        lock (walletLock) {
            delta = SetMeretLocked(value);
        }
        if (reportSpent) {
            ReportMeretSpent(delta);
        }
    }

    // Adds delta to the merets (a negative delta takes), as `Meret += delta` did, but in one step. A take of more than
    // is held throws, as the setter does, and changes nothing. reportSpent as SetMeret.
    public void ChangeMeret(long delta, bool reportSpent = true) {
        long change;
        lock (walletLock) {
            long value = Sum(Currency.Meret, delta);
            if (value < 0) {
                throw new ArgumentException("Not enough Merets");
            }
            change = SetMeretLocked(value);
        }
        if (reportSpent) {
            ReportMeretSpent(change);
        }
    }

    // Takes amount when at least that much is held, checked and taken in one step (packet and use_merat as a take).
    // With notify false nothing is sent or reported: the caller calls NotifyMeretTaken once what was paid for is
    // stored, or ReturnMeret. The merets are taken on other threads too (a wedding hall paid by the partner), so a
    // payer's own check of the balance can be out of date at its take: a take there goes through this, not
    // ChangeMeret, which throws. reportSpent false: the packet is sent and the caller reports the spend itself.
    public bool TryTakeMeret(long amount, bool notify = true, bool reportSpent = true) {
        if (amount < 0) {
            return false;
        }
        long change;
        lock (walletLock) {
            if (Currency.Meret < amount) {
                return false;
            }
            if (!notify) {
                Currency.Meret -= amount;
                return true;
            }
            change = SetMeretLocked(Currency.Meret - amount);
        }
        if (reportSpent) {
            ReportMeretSpent(change);
        }
        return true;
    }

    // The packet and use_merat of a TryTakeMeret(amount, notify: false). report false: the packet only; the caller
    // reports the spend (ReportMeretSpent) once what it paid for is given.
    public void NotifyMeretTaken(long amount, bool report = true) {
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateMeret(Currency, -amount));
        }
        if (report) {
            ReportMeretSpent(-amount);
        }
    }

    // Gives back a TryTakeMeret(amount, notify: false) and sends the balance (a Meret packet sent meanwhile showed it
    // without the take). Chosen here: sent with a change of 0, as the take was never shown. As ReturnMeso, the
    // give-back stops at MaxMeret and a part over it is logged.
    public void ReturnMeret(long amount) {
        if (amount <= 0) {
            return;
        }
        long over;
        lock (walletLock) {
            long room = Constant.MaxMeret - Currency.Meret;
            over = Math.Max(0, amount - room);
            Currency.Meret += amount - over;
            session.Send(CurrencyPacket.UpdateMeret(Currency, 0));
        }
        if (over > 0) {
            logger.Warning("Meret give-back of {Amount} for {CharacterId} over the cap by {Over}", amount, session.CharacterId, over);
        }
    }

    private long SetMeretLocked(long value) {
        long delta = Math.Min(value, Constant.MaxMeret) - Currency.Meret;
        Currency.Meret = Math.Min(value, Constant.MaxMeret);
        session.Send(CurrencyPacket.UpdateMeret(Currency, delta));
        return delta;
    }

    // use_merat (trophy 23200041) counts the merets spent: KMS2 sends its update right after each Meret packet with
    // a negative change, the value up by the amount spent.
    public void ReportMeretSpent(long delta) {
        long spent = TrophyReportRules.MeretSpent(delta);
        if (spent > 0) {
            session.ConditionUpdate(ConditionType.use_merat, counter: spent);
        }
    }

    public long GameMeret {
        get => Currency.GameMeret;
        set {
            if (value < 0) {
                throw new ArgumentException("Not enough RedMerets");
            }

            long delta;
            lock (walletLock) {
                delta = SetGameMeretLocked(value);
            }
            ReportMeretSpent(delta);
        }
    }

    // Adds delta to the red merets in one step, as ChangeMeret.
    public void ChangeGameMeret(long delta) {
        long change;
        lock (walletLock) {
            long value = Sum(Currency.GameMeret, delta);
            if (value < 0) {
                throw new ArgumentException("Not enough RedMerets");
            }
            change = SetGameMeretLocked(value);
        }
        ReportMeretSpent(change);
    }

    // TryTakeMeret, NotifyMeretTaken and ReturnMeret for the red merets. Their packet is MeretUse, not Meret.
    public bool TryTakeGameMeret(long amount, bool notify = true, bool reportSpent = true) {
        if (amount < 0) {
            return false;
        }
        long change;
        lock (walletLock) {
            if (Currency.GameMeret < amount) {
                return false;
            }
            if (!notify) {
                Currency.GameMeret -= amount;
                return true;
            }
            change = SetGameMeretLocked(Currency.GameMeret - amount);
        }
        if (reportSpent) {
            ReportMeretSpent(change);
        }
        return true;
    }

    // The client's currency 10 (HasEnoughCurrency and GetCurrencyAmount, uicommon.cpp:972/1020): the red merets
    // when they cover the amount, else the plain merets. Each wallet is taken in one step; red tells which one paid.
    public bool TryTakeRedElseMeret(long amount, out bool red, bool notify = true) {
        red = TryTakeGameMeret(amount, notify);
        return red || TryTakeMeret(amount, notify);
    }

    // Takes a meret cost from the wallet the client checks it against (MeretCost.Wallet); red tells which one paid.
    public bool TryTakeMerets(MeretCost.Wallet wallet, long amount, out bool red, bool notify = true) {
        switch (wallet) {
            case MeretCost.Wallet.Meret:
                red = false;
                return TryTakeMeret(amount, notify);
            case MeretCost.Wallet.Red:
                red = true;
                return TryTakeGameMeret(amount, notify);
            default:
                return TryTakeRedElseMeret(amount, out red, notify);
        }
    }

    public void NotifyGameMeretTaken(long amount, bool report = true) {
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateGameMeret(Currency, -amount));
        }
        if (report) {
            ReportMeretSpent(-amount);
        }
    }

    public void ReturnGameMeret(long amount) {
        if (amount <= 0) {
            return;
        }
        long over;
        lock (walletLock) {
            long room = Constant.MaxMeret - Currency.GameMeret;
            over = Math.Max(0, amount - room);
            Currency.GameMeret += amount - over;
            session.Send(CurrencyPacket.UpdateGameMeret(Currency, 0));
        }
        if (over > 0) {
            logger.Warning("Red meret give-back of {Amount} for {CharacterId} over the cap by {Over}", amount, session.CharacterId, over);
        }
    }

    private long SetGameMeretLocked(long value) {
        long delta = Math.Min(value, Constant.MaxMeret) - Currency.GameMeret;
        Currency.GameMeret = Math.Min(value, Constant.MaxMeret);
        session.Send(CurrencyPacket.UpdateGameMeret(Currency, delta));
        return delta;
    }

    // public long EventMeret {
    //     get => Currency.EventMeret;
    //     private set => Currency.EventMeret = value;
    // }

    // A read and a set: a change of the balance goes through ChangeMeso, TryTakeMeso or AddMeso (merets: ChangeMeret,
    // TryTakeMeret, ChangeGameMeret; the other wallets: Change).
    public long Meso {
        get => Currency.Meso;
        set {
            if (value < 0) {
                throw new ArgumentException("Not enough Mesos");
            }

            lock (walletLock) {
                Currency.Meso = Math.Min(value, Constant.MaxMeso);
                SendMeso();
            }
            // meso_donation quests count the meso held (QuestManager.UpdateMeso).
            session.Quest?.UpdateMeso();
        }
    }

    // Adds delta to the balance (a negative delta takes), as `Meso += delta` did, but in one step. A take of more
    // than is held throws, as the setter does, and changes nothing.
    public void ChangeMeso(long delta) {
        lock (walletLock) {
            long value = Sum(Currency.Meso, delta);
            if (value < 0) {
                throw new ArgumentException("Not enough Mesos");
            }
            Currency.Meso = Math.Min(value, Constant.MaxMeso);
            SendMeso();
        }
        session.Quest?.UpdateMeso();
    }

    // Takes amount when at least that much is held, checked and taken in one step. With notify false nothing is sent
    // (the caller sends the balance with NotifyMeso when the rest of its change is done, or gives it back).
    public bool TryTakeMeso(long amount, bool notify = true) {
        if (amount < 0) {
            return false;
        }
        lock (walletLock) {
            if (Currency.Meso < amount) {
                return false;
            }
            Currency.Meso -= amount;
            if (notify) {
                SendMeso();
            }
        }
        if (notify) {
            session.Quest?.UpdateMeso();
        }
        return true;
    }

    // Gives back a take made with TryTakeMeso(notify: false) and sends the balance: a meso packet sent meanwhile
    // (a gain on the field's thread) showed the balance without the take. Chosen here: the give-back stops at
    // MaxMeso as every gain does, and a part over it (a gain reached the cap meanwhile) is logged.
    // With notify false nothing is sent: a refusal that answers as before the take, when no packet showed the take.
    public void ReturnMeso(long amount, bool notify = true) {
        GiveBack(amount, notify, sendsBefore: null);
    }

    // notify, or with sendsBefore set: whether a meso packet went out since then, read under the same lock as the add.
    private void GiveBack(long amount, bool notify, long? sendsBefore) {
        if (amount <= 0) {
            return;
        }
        long over;
        lock (walletLock) {
            if (sendsBefore != null) {
                notify = mesoSends != sendsBefore;
            }
            // Room left counted first: MaxMeso is long.MaxValue, so the sum itself could overflow.
            long room = Constant.MaxMeso - Currency.Meso;
            over = Math.Max(0, amount - room);
            Currency.Meso += amount - over;
            if (notify) {
                SendMeso();
            }
        }
        if (notify) {
            session.Quest?.UpdateMeso();
        }
        if (over > 0) {
            logger.Warning("Meso give-back of {Amount} for {CharacterId} over the cap by {Over}", amount, session.CharacterId, over);
        }
    }

    // The meso packets sent so far: read before a TryTakeMeso(notify: false) and passed to ReturnHeldMeso.
    public long MesoSends {
        get {
            lock (walletLock) {
                return mesoSends;
            }
        }
    }

    // Gives back a held take (TryTakeMeso with notify false) on a refusal: silent when no meso packet went out since
    // sendsBefore (the client still shows the balance before the take), else with the balance, since a packet sent
    // meanwhile (a gain on the field's thread) showed it without the price.
    public void ReturnHeldMeso(long amount, long sendsBefore) {
        GiveBack(amount, notify: false, sendsBefore);
    }

    // Takes amount, or all that is held when that is less, in one step, and returns what was taken. Nothing is sent
    // (the caller sends the balance with NotifyMeso).
    public long TakeMesoUpTo(long amount) {
        if (amount <= 0) {
            return 0;
        }
        lock (walletLock) {
            long taken = Math.Min(amount, Currency.Meso);
            Currency.Meso -= taken;
            return taken;
        }
    }

    // Adds mesos and merets inside a purchase transaction: nothing is sent and no quest is counted there, since a
    // quest it completes starts a purchase save of its own, which would wait on this one's account lock and rows.
    // Returns what was added (each capped at its maximum); the caller sends it with NotifyAdded once the save is
    // stored, or takes it back with TakeBackAdded.
    public (long Meso, long Meret, long GameMeret) AddUnsent(long meso, long meret, long gameMeret) {
        lock (walletLock) {
            (long Meso, long Meret, long GameMeret) before = (Currency.Meso, Currency.Meret, Currency.GameMeret);
            if (meso > 0) {
                Currency.Meso = Math.Min(Sum(Currency.Meso, meso), Constant.MaxMeso);
            }
            if (meret > 0) {
                Currency.Meret = Math.Min(Sum(Currency.Meret, meret), Constant.MaxMeret);
            }
            if (gameMeret > 0) {
                Currency.GameMeret = Math.Min(Sum(Currency.GameMeret, gameMeret), Constant.MaxMeret);
            }
            return (Currency.Meso - before.Meso, Currency.Meret - before.Meret, Currency.GameMeret - before.GameMeret);
        }
    }

    // The packets of an AddUnsent, as ChangeMeso, ChangeMeret and ChangeGameMeret send them, and the meso quests.
    public void NotifyAdded((long Meso, long Meret, long GameMeret) added) {
        lock (walletLock) {
            if (added.Meso != 0) {
                SendMeso();
            }
            if (added.Meret != 0) {
                session.Send(CurrencyPacket.UpdateMeret(Currency, added.Meret));
            }
            if (added.GameMeret != 0) {
                session.Send(CurrencyPacket.UpdateGameMeret(Currency, added.GameMeret));
            }
        }
        if (added.Meso != 0) {
            session.Quest?.UpdateMeso();
        }
    }

    // Takes back an AddUnsent whose save was not stored. Chosen here: the balances are sent (change 0), since a
    // packet sent meanwhile on another thread may have shown the add; a part already spent meanwhile stays spent.
    public void TakeBackAdded((long Meso, long Meret, long GameMeret) added) {
        lock (walletLock) {
            if (added.Meso != 0) {
                Currency.Meso = Math.Max(0, Currency.Meso - added.Meso);
                SendMeso();
            }
            if (added.Meret != 0) {
                Currency.Meret = Math.Max(0, Currency.Meret - added.Meret);
                session.Send(CurrencyPacket.UpdateMeret(Currency, 0));
            }
            if (added.GameMeret != 0) {
                Currency.GameMeret = Math.Max(0, Currency.GameMeret - added.GameMeret);
                session.Send(CurrencyPacket.UpdateGameMeret(Currency, 0));
            }
        }
        if (added.Meso != 0) {
            session.Quest?.UpdateMeso();
        }
    }

    // A token gain made by AddUnsent(type, amount): what was added (capped) and the condition that counts it.
    public readonly record struct Added(CurrencyType Type, long Amount, ConditionType? Condition);

    // AddUnsent for a token currency (MentorToken and the like): added up to its cap inside a purchase transaction,
    // with no packet and no count of its condition (get_mentor_token and the like); NotifyAdded sends and counts it
    // once the save is stored, TakeBackAdded takes it back when it is not.
    public Added AddUnsent(CurrencyType type, long amount) {
        if (amount <= 0) {
            return new Added(type, 0, null);
        }
        lock (walletLock) {
            long added = SetLocked(type, Sum(this[type], amount), out ConditionType? condition, send: false);
            return new Added(type, added, condition);
        }
    }

    public void NotifyAdded(Added added) {
        if (added.Amount <= 0) {
            return;
        }
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateCurrency(Currency, added.Type, added.Amount, 0));
        }
        Report(added.Condition, added.Amount);
    }

    // As TakeBackAdded above: the balance is sent, a part spent meanwhile stays spent.
    public void TakeBackAdded(Added added) {
        if (added.Amount <= 0) {
            return;
        }
        lock (walletLock) {
            SetLocked(added.Type, Math.Max(0, this[added.Type] - added.Amount), out _);
        }
    }

    // Sends the balance (after TryTakeMeso with notify false).
    public void NotifyMeso() {
        lock (walletLock) {
            SendMeso();
        }
        session.Quest?.UpdateMeso();
    }

    // A meso gain sent with the code of its kind (see CurrencyPacket.UpdateMeso).
    public void AddMeso(long amount, ExpMessageCode code) {
        if (amount <= 0) {
            return;
        }
        lock (walletLock) {
            Currency.Meso = Math.Min(Sum(Currency.Meso, amount), Constant.MaxMeso);
            SendMeso((int) code);
        }
        session.Quest?.UpdateMeso();
    }

    // balance + delta, a gain past long.MaxValue held there: MaxMeso and MaxMeret are long.MaxValue, so the sum
    // wrapped to a negative balance (AddMeso set it; the Change methods threw, the gain lost).
    private static long Sum(long balance, long delta) {
        return delta > 0 && balance > long.MaxValue - delta ? long.MaxValue : balance + delta;
    }

    // Sends the balance; the caller holds walletLock.
    private void SendMeso(int code = 0) {
        mesoSends++;
        session.Send(CurrencyPacket.UpdateMeso(Currency, code));
    }

    public long CanAddMeso(long amount) {
        return amount >= 0
            ? Math.Min(amount, Constant.MaxMeso - Currency.Meso)
            : Math.Max(amount, -Currency.Meso);
    }

    public long CanAddMeret(long amount) {
        return amount >= 0
            ? Math.Min(amount, Constant.MaxMeret - Currency.Meret)
            : Math.Max(amount, -Currency.Meret);
    }

    public long CanAddGameMeret(long amount) {
        return amount >= 0
            ? Math.Min(amount, Constant.MaxMeret - Currency.GameMeret)
            : Math.Max(amount, -Currency.GameMeret);
    }

    public long this[CurrencyType type] {
        get => type switch {
            CurrencyType.ValorToken => Currency.ValorToken,
            CurrencyType.Treva => Currency.Treva,
            CurrencyType.Rue => Currency.Rue,
            CurrencyType.HaviFruit => Currency.HaviFruit,
            CurrencyType.ReverseCoin => Currency.ReverseCoin,
            CurrencyType.MentorToken => Currency.MentorToken,
            CurrencyType.MenteeToken => Currency.MenteeToken,
            CurrencyType.StarPoint => Currency.StarPoint,
            CurrencyType.MesoToken => Currency.MesoToken,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid currency type."),
        };
        set {
            if (value < 0) {
                throw new ArgumentException($"Not enough {type}");
            }

            long delta;
            ConditionType? condition;
            lock (walletLock) {
                delta = SetLocked(type, value, out condition);
            }
            Report(condition, delta);
        }
    }

    // Adds delta to the wallet of the type (a negative delta takes), as `Currency[type] += delta` did, but in one
    // step: the clamp, the overflow, the packet and the count of a gain are the setter's. A take of more than is held
    // throws, as the setter does, and changes nothing.
    public void Change(CurrencyType type, long delta) {
        long change;
        ConditionType? condition;
        lock (walletLock) {
            long value = Sum(this[type], delta);
            if (value < 0) {
                throw new ArgumentException($"Not enough {type}");
            }
            change = SetLocked(type, value, out condition);
        }
        Report(condition, change);
    }

    // Takes amount from the wallet of the type in one step when that much is held, as Change with -amount; false and
    // nothing changed when it is not. A take that a later step can refuse (a sale saved after it) goes first, so a
    // spend on another thread between the check and the take cannot leave the sale stored and the take refused.
    public bool TryTake(CurrencyType type, long amount) {
        if (amount < 0) {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A take is not negative.");
        }
        long change;
        ConditionType? condition;
        lock (walletLock) {
            if (this[type] < amount) {
                return false;
            }
            change = SetLocked(type, this[type] - amount, out condition);
        }
        Report(condition, change);
        return true;
    }

    // Gives back a TryTake(type, amount) whose purchase was not stored: added in one step as a gain is, with the balance
    // packet, but not counted by the gain's condition (get_honor_token and the like), since nothing was gained. The
    // give-back stops at the currency's cap as every gain does; a part over it is logged.
    public void Return(CurrencyType type, long amount) {
        if (amount <= 0) {
            return;
        }
        long change;
        lock (walletLock) {
            change = SetLocked(type, Sum(this[type], amount), out _);
        }
        if (change < amount) {
            logger.Warning("{Type} give-back of {Amount} for {CharacterId} over the cap by {Over}", type, amount, session.CharacterId, amount - change);
        }
    }

    // A gain is counted by its condition (get_honor_token and the like) after the lock is let go. The balance packet
    // is sent under the lock (SetLocked), as the meso and meret packets are: each carries the whole balance, and one
    // sent after the lock could reach the client after a later change's packet, showing a stale balance. Chosen here:
    // the packet now comes before the condition's updates (the setter sent the condition first).
    private void Report(ConditionType? condition, long delta) {
        if (condition != null && delta > 0) {
            session.ConditionUpdate(condition.Value, delta);
        }
    }

    // send: the balance packet (AddUnsent leaves it to NotifyAdded).
    private long SetLocked(CurrencyType type, long value, out ConditionType? condition, bool send = true) {
        condition = null;
        long delta;
        long overflow;
        switch (type) {
            case CurrencyType.ValorToken:
                delta = Math.Min(value, Constants.HonorTokenMax) - Currency.ValorToken;
                overflow = Math.Max(0, value - Constants.HonorTokenMax);
                Currency.ValorToken = Math.Min(value, Constants.HonorTokenMax);
                condition = ConditionType.get_honor_token;
                break;
            case CurrencyType.Treva:
                delta = Math.Min(value, Constants.KarmaTokenMax) - Currency.Treva;
                overflow = Math.Max(0, value - Constants.KarmaTokenMax);
                Currency.Treva = Math.Min(value, Constants.KarmaTokenMax);
                condition = ConditionType.get_karma_token;
                break;
            case CurrencyType.Rue:
                delta = Math.Min(value, Constants.LuTokenMax) - Currency.Rue;
                overflow = Math.Max(0, value - Constants.LuTokenMax);
                Currency.Rue = Math.Min(value, Constants.LuTokenMax);
                condition = ConditionType.get_lu_token;
                break;
            case CurrencyType.HaviFruit:
                delta = Math.Min(value, Constants.HabiTokenMax) - Currency.HaviFruit;
                overflow = Math.Max(0, value - Constants.HabiTokenMax);
                Currency.HaviFruit = Math.Min(value, Constants.HabiTokenMax);
                condition = ConditionType.get_habi_token;
                break;
            case CurrencyType.ReverseCoin:
                delta = Math.Min(value, Constants.ReverseCoinMax) - Currency.ReverseCoin;
                overflow = Math.Max(0, value - Constants.ReverseCoinMax);
                Currency.ReverseCoin = Math.Min(value, Constants.ReverseCoinMax);
                condition = ConditionType.get_reverse_coin;
                break;
            case CurrencyType.MentorToken:
                delta = Math.Min(value, Constants.MentorTokenMax) - Currency.MentorToken;
                overflow = Math.Max(0, value - Constants.MentorTokenMax);
                Currency.MentorToken = Math.Min(value, Constants.MentorTokenMax);
                condition = ConditionType.get_mentor_token;
                break;
            case CurrencyType.MenteeToken:
                delta = Math.Min(value, Constants.MenteeTokenMax) - Currency.MenteeToken;
                overflow = Math.Max(0, value - Constants.MenteeTokenMax);
                Currency.MenteeToken = Math.Min(value, Constants.MenteeTokenMax);
                condition = ConditionType.get_mentee_token;
                break;
            case CurrencyType.StarPoint:
                delta = Math.Min(value, Constant.StarPointMax) - Currency.StarPoint;
                overflow = Math.Max(0, value - Constant.StarPointMax);
                Currency.StarPoint = Math.Min(value, Constant.StarPointMax);
                condition = ConditionType.get_star_point;
                break;
            case CurrencyType.MesoToken:
                delta = Math.Min(value, Constant.MesoTokenMax) - Currency.MesoToken;
                overflow = Math.Max(0, value - Constant.MesoTokenMax);
                Currency.MesoToken = Math.Min(value, Constant.MesoTokenMax);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid currency type.");
        }

        if (send) {
            session.Send(CurrencyPacket.UpdateCurrency(Currency, type, delta, overflow));
        }
        return delta;
    }
}
