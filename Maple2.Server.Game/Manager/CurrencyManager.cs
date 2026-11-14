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
            long value = Currency.Meret + delta;
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

    // The packet and use_merat of a TryTakeMeret(amount, notify: false).
    public void NotifyMeretTaken(long amount) {
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateMeret(Currency, -amount));
        }
        ReportMeretSpent(-amount);
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
            long value = Currency.GameMeret + delta;
            if (value < 0) {
                throw new ArgumentException("Not enough RedMerets");
            }
            change = SetGameMeretLocked(value);
        }
        ReportMeretSpent(change);
    }

    // TryTakeMeret, NotifyMeretTaken and ReturnMeret for the red merets. Their packet is MeretUse, not Meret.
    public bool TryTakeGameMeret(long amount, bool notify = true) {
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
        ReportMeretSpent(change);
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

    public void NotifyGameMeretTaken(long amount) {
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateGameMeret(Currency, -amount));
        }
        ReportMeretSpent(-amount);
    }

    public void ReturnGameMeret(long amount) {
        if (amount <= 0) {
            return;
        }
        lock (walletLock) {
            Currency.GameMeret += Math.Min(amount, Constant.MaxMeret - Currency.GameMeret);
            session.Send(CurrencyPacket.UpdateGameMeret(Currency, 0));
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
            long value = Currency.Meso + delta;
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
            Currency.Meso = Math.Min(Currency.Meso + amount, Constant.MaxMeso);
            SendMeso((int) code);
        }
        session.Quest?.UpdateMeso();
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
            long value = this[type] + delta;
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

    // A gain is counted by its condition (get_honor_token and the like) after the lock is let go. The balance packet
    // is sent under the lock (SetLocked), as the meso and meret packets are: each carries the whole balance, and one
    // sent after the lock could reach the client after a later change's packet, showing a stale balance. Chosen here:
    // the packet now comes before the condition's updates (the setter sent the condition first).
    private void Report(ConditionType? condition, long delta) {
        if (condition != null && delta > 0) {
            session.ConditionUpdate(condition.Value, delta);
        }
    }

    private long SetLocked(CurrencyType type, long value, out ConditionType? condition) {
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

        session.Send(CurrencyPacket.UpdateCurrency(Currency, type, delta, overflow));
        return delta;
    }
}
