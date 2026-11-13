using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
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
    // stored, or ReturnMeret.
    public bool TryTakeMeret(long amount, bool notify = true) {
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
        ReportMeretSpent(change);
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
    // without the take). Chosen here: sent with a change of 0, as the take was never shown.
    public void ReturnMeret(long amount) {
        if (amount <= 0) {
            return;
        }
        lock (walletLock) {
            Currency.Meret += Math.Min(amount, Constant.MaxMeret - Currency.Meret);
            session.Send(CurrencyPacket.UpdateMeret(Currency, 0));
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

    // TryTakeMeret, NotifyMeretTaken and ReturnMeret for the red merets.
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

    public void NotifyGameMeretTaken(long amount) {
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateMeret(Currency, -amount));
        }
        ReportMeretSpent(-amount);
    }

    public void ReturnGameMeret(long amount) {
        if (amount <= 0) {
            return;
        }
        lock (walletLock) {
            Currency.GameMeret += Math.Min(amount, Constant.MaxMeret - Currency.GameMeret);
            session.Send(CurrencyPacket.UpdateMeret(Currency, 0));
        }
    }

    private long SetGameMeretLocked(long value) {
        long delta = Math.Min(value, Constant.MaxMeret) - Currency.GameMeret;
        Currency.GameMeret = Math.Min(value, Constant.MaxMeret);
        session.Send(CurrencyPacket.UpdateMeret(Currency, delta));
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
                session.Send(CurrencyPacket.UpdateMeso(Currency));
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
            session.Send(CurrencyPacket.UpdateMeso(Currency));
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
                session.Send(CurrencyPacket.UpdateMeso(Currency));
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
    public void ReturnMeso(long amount) {
        if (amount <= 0) {
            return;
        }
        long over;
        lock (walletLock) {
            // Room left counted first: MaxMeso is long.MaxValue, so the sum itself could overflow.
            long room = Constant.MaxMeso - Currency.Meso;
            over = Math.Max(0, amount - room);
            Currency.Meso += amount - over;
            session.Send(CurrencyPacket.UpdateMeso(Currency));
        }
        session.Quest?.UpdateMeso();
        if (over > 0) {
            logger.Warning("Meso give-back of {Amount} for {CharacterId} over the cap by {Over}", amount, session.CharacterId, over);
        }
    }

    // Sends the balance (after TryTakeMeso with notify false).
    public void NotifyMeso() {
        lock (walletLock) {
            session.Send(CurrencyPacket.UpdateMeso(Currency));
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
            session.Send(CurrencyPacket.UpdateMeso(Currency, (int) code));
        }
        session.Quest?.UpdateMeso();
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
            ByteWriter packet;
            lock (walletLock) {
                delta = SetLocked(type, value, out condition, out packet);
            }
            Report(condition, delta, packet);
        }
    }

    // Adds delta to the wallet of the type (a negative delta takes), as `Currency[type] += delta` did, but in one
    // step: the clamp, the overflow, the packet and the count of a gain are the setter's. A take of more than is held
    // throws, as the setter does, and changes nothing.
    public void Change(CurrencyType type, long delta) {
        long change;
        ConditionType? condition;
        ByteWriter packet;
        lock (walletLock) {
            long value = this[type] + delta;
            if (value < 0) {
                throw new ArgumentException($"Not enough {type}");
            }
            change = SetLocked(type, value, out condition, out packet);
        }
        Report(condition, change, packet);
    }

    // A gain is counted by its condition (get_honor_token and the like) after the lock is let go, then the balance
    // packet built under the lock is sent: the order the setter sent them in.
    private void Report(ConditionType? condition, long delta, ByteWriter packet) {
        if (condition != null && delta > 0) {
            session.ConditionUpdate(condition.Value, delta);
        }
        session.Send(packet);
    }

    private long SetLocked(CurrencyType type, long value, out ConditionType? condition, out ByteWriter packet) {
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

        packet = CurrencyPacket.UpdateCurrency(Currency, type, delta, overflow);
        return delta;
    }
}
