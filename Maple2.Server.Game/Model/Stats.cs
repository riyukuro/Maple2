using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;

namespace Maple2.Server.Game.Model;

public class Stats {
    public const int BASIC_TOTAL = 35;
    public const int SPECIAL_TOTAL = 180;

    private readonly Dictionary<BasicAttribute, Stat> basicValues;
    private readonly Dictionary<SpecialAttribute, Stat> specialValues;

    public int GearScore = 0;

    public Stats(IReadOnlyDictionary<BasicAttribute, long> statsDictionary, JobCode jobCode) {
        basicValues = new Dictionary<BasicAttribute, Stat>();
        specialValues = new Dictionary<SpecialAttribute, Stat>();

        foreach (BasicAttribute attribute in statsDictionary.Keys) {
            if (attribute is BasicAttribute.PhysicalAtk or BasicAttribute.MagicalAtk) {
                continue;
            }
            this[attribute].AddBase(statsDictionary[attribute]);
        }

        // TODO: Remove when UserStat has rmsp
        this[BasicAttribute.MountSpeed].AddBase(100);

        AddDerivedBase(jobCode);
    }

    public Stats(NpcMetadataStat npcStats) {
        basicValues = new Dictionary<BasicAttribute, Stat>();
        specialValues = new Dictionary<SpecialAttribute, Stat>();
        foreach ((BasicAttribute attribute, long value) in npcStats.Stats) {
            this[attribute].AddBase(value);
        }
    }

    /// <summary>
    /// Clears and sets static stats.
    /// </summary>
    public void SetStaticStats(IReadOnlyDictionary<BasicAttribute, long> statsDictionary) {
        HoldCurrent();
        ClearStats();
        foreach (BasicAttribute attribute in statsDictionary.Keys) {
            this[attribute].AddBase(statsDictionary[attribute]);
        }
        EndReset(false);
        // Does not add PhysicalAtk or MagicalAtk due to it not using jobCode
    }

    /// <summary>
    /// Starts an npc's rebuild on its own stats: every Current keeps its value until <see cref="EndReset"/>.
    /// </summary>
    public void Reset(IReadOnlyDictionary<BasicAttribute, long> npcStats) {
        HoldCurrent();
        ClearStats();
        foreach ((BasicAttribute attribute, long value) in npcStats) {
            this[attribute].AddBase(value);
        }
    }

    /// <summary>
    /// Starts a rebuild: every Current keeps its value until <see cref="EndReset"/>.
    /// </summary>
    public void Reset(IReadOnlyDictionary<BasicAttribute, long> metadata, JobCode jobCode) {
        HoldCurrent();
        ClearStats();

        foreach (BasicAttribute attribute in metadata.Keys) {
            if (attribute is BasicAttribute.PhysicalAtk or BasicAttribute.MagicalAtk) {
                continue;
            }
            this[attribute].AddBase(metadata[attribute]);
        }

        // TODO: Remove when UserStat has rmsp
        this[BasicAttribute.MountSpeed].AddBase(100);

        AddDerivedBase(jobCode);
    }

    [Obsolete("Use Reset(UserStatMetadata, JobCode) instead.")]
    public void Reset(JobCode jobCode, short level) {
        HoldCurrent();
        ClearStats();

        this[BasicAttribute.Strength].AddBase(BaseStat.Strength(jobCode, level));
        this[BasicAttribute.Dexterity].AddBase(BaseStat.Dexterity(jobCode, level));
        this[BasicAttribute.Intelligence].AddBase(BaseStat.Intelligence(jobCode, level));
        this[BasicAttribute.Luck].AddBase(BaseStat.Luck(jobCode, level));
        this[BasicAttribute.Health].AddBase(BaseStat.Health(jobCode, level));
        this[BasicAttribute.HpRegen] = new Stat(10);
        this[BasicAttribute.HpRegenInterval] = new Stat(3000);
        this[BasicAttribute.Spirit] = new Stat(100);
        this[BasicAttribute.SpRegen] = new Stat(10);
        this[BasicAttribute.SpRegenInterval] = new Stat(1000);
        this[BasicAttribute.Stamina] = new Stat(120);
        this[BasicAttribute.StaminaRegen] = new Stat(10);
        this[BasicAttribute.StaminaRegenInterval] = new Stat(500);
        this[BasicAttribute.AttackSpeed] = new Stat(100);
        this[BasicAttribute.MovementSpeed] = new Stat(100);
        this[BasicAttribute.Accuracy].AddBase(BaseStat.Accuracy(jobCode, level));
        this[BasicAttribute.Evasion].AddBase(BaseStat.Evasion(jobCode, level));
        this[BasicAttribute.CriticalRate].AddBase(BaseStat.CriticalRate(jobCode, level));
        this[BasicAttribute.CriticalDamage].AddBase(BaseStat.CriticalDamage(jobCode, level));
        this[BasicAttribute.CriticalEvasion].AddBase(BaseStat.CriticalEvasion(jobCode, level));
        this[BasicAttribute.Defense].AddBase(BaseStat.Defense(jobCode, level));
        this[BasicAttribute.JumpHeight] = new Stat(100);
        this[BasicAttribute.PhysicalRes].AddBase(BaseStat.PhysicalRes(jobCode, level));
        this[BasicAttribute.MagicalRes].AddBase(BaseStat.MagicalRes(jobCode, level));
        this[BasicAttribute.MountSpeed] = new Stat(100);

        this[BasicAttribute.PhysicalAtk].AddBase(
            AttackStat.PhysicalAtk(jobCode, this[BasicAttribute.Strength].Base, this[BasicAttribute.Dexterity].Base, this[BasicAttribute.Luck].Base));
        this[BasicAttribute.MagicalAtk].AddBase(AttackStat.MagicalAtk(jobCode, this[BasicAttribute.Intelligence].Base));

#if DEBUG
        this[BasicAttribute.AttackSpeed].AddTotal(40);
        this[BasicAttribute.MovementSpeed].AddTotal(40);
        this[BasicAttribute.JumpHeight].AddTotal(40);
        this[BasicAttribute.MountSpeed].AddTotal(60);
#endif
    }

    // The attack and resistance that come from the base str, dex, int and luk (AttackStat).
    private void AddDerivedBase(JobCode jobCode) {
        (long patk, long matk, long pres, long mres) = Derived(jobCode, total: false);
        this[BasicAttribute.PhysicalAtk].AddBase(patk);
        this[BasicAttribute.MagicalAtk].AddBase(matk);
        this[BasicAttribute.PhysicalRes].AddBase(pres);
        this[BasicAttribute.MagicalRes].AddBase(mres);
    }

    /// <summary>
    /// Adds what the attack and resistance gain from the str, dex, int and luk above the base (spent points, items,
    /// buffs): in KMS2 their totals follow the total stats. Call after every stat source is added and before <see cref="Total"/>.
    /// Chosen here: a rate bonus on str, dex, int or luk counts.
    /// </summary>
    public void AddDerivedTotal(JobCode jobCode) {
        (long basePatk, long baseMatk, long basePres, long baseMres) = Derived(jobCode, total: false);
        (long patk, long matk, long pres, long mres) = Derived(jobCode, total: true);
        this[BasicAttribute.PhysicalAtk].AddTotal(patk - basePatk);
        this[BasicAttribute.MagicalAtk].AddTotal(matk - baseMatk);
        this[BasicAttribute.PhysicalRes].AddTotal(pres - basePres);
        this[BasicAttribute.MagicalRes].AddTotal(mres - baseMres);
    }

    private (long PhysicalAtk, long MagicalAtk, long PhysicalRes, long MagicalRes) Derived(JobCode jobCode, bool total) {
        long Value(BasicAttribute attribute) {
            Stat stat = this[attribute];
            return total ? stat.Total + (long) (stat.Rate * stat.Total) : stat.Base;
        }
        long str = Value(BasicAttribute.Strength);
        long dex = Value(BasicAttribute.Dexterity);
        long @int = Value(BasicAttribute.Intelligence);
        long luk = Value(BasicAttribute.Luck);
        return (AttackStat.PhysicalAtk(jobCode, str, dex, luk), AttackStat.MagicalAtk(jobCode, @int),
            AttackStat.PhysicalRes(jobCode, str, dex), AttackStat.MagicalRes(jobCode, @int));
    }

    /// <summary>
    /// Adds spent attribute points to the totals: 1 str, dex, int or luk, 10 health or 3 critical rate a point.
    /// </summary>
    public void AddAllocation(BasicAttribute attribute, int points) {
        switch (attribute) {
            case BasicAttribute.Strength:
            case BasicAttribute.Dexterity:
            case BasicAttribute.Intelligence:
            case BasicAttribute.Luck:
                this[attribute].AddTotal(points);
                break;
            case BasicAttribute.Health:
                this[attribute].AddTotal(10L * points);
                break;
            case BasicAttribute.CriticalRate:
                this[attribute].AddTotal(3L * points);
                break;
        }
    }

    /// <summary>
    ///  Apply rate bonus to total value of each stat
    /// </summary>
    public void Total() {
        foreach (Stat stat in basicValues.Values) {
            long rateBonus = (long) (stat.Rate * (stat.Base + (stat.Total - stat.Base)));
            stat.AddTotal(rateBonus);
        }

        foreach (Stat stat in specialValues.Values) {
            long rateBonus = (long) (stat.Rate * (stat.Base + (stat.Total - stat.Base)));
            stat.AddTotal(rateBonus);
        }
    }

    public Stat this[BasicAttribute attribute] {
        get {
            if (!basicValues.ContainsKey(attribute)) {
                basicValues[attribute] = new Stat(0, 0, 0);
            }

            return basicValues[attribute];
        }
        set => basicValues[attribute] = value;
    }

    public Stat this[SpecialAttribute attribute] {
        get {
            if (!specialValues.ContainsKey(attribute)) {
                specialValues[attribute] = new Stat(0, 0, 0) { Signed = true };
            }

            return specialValues[attribute];
        }
        set => specialValues[attribute] = value;
    }

    // Another thread (the field's Actor.Update death check) reads Health.Current while a rebuild runs, and the rebuild
    // passes through 0 (ClearStats) on its way to the new totals. A held stat keeps its Current through the rebuild.
    private void HoldCurrent() {
        foreach (Stat stat in basicValues.Values) {
            stat.Hold();
        }
        foreach (Stat stat in specialValues.Values) {
            stat.Hold();
        }
    }

    /// <summary>
    /// Ends a rebuild: each Current keeps its share of the Total, as KMS2 does when a buff, equip or pet changes the
    /// maximum (Current x new Total / old Total, rounded down); Health goes to 0 instead when <paramref name="dead"/>.
    /// On a <paramref name="levelUp"/> a living actor's Health and Spirit fill to the new Total, as KMS2 sends after a level up.
    /// </summary>
    public void EndReset(bool dead, bool levelUp = false) {
        foreach ((BasicAttribute attribute, Stat stat) in basicValues) {
            long before = stat.Current;
            stat.EndHold();
            if (!dead && attribute == BasicAttribute.Health && before > 0 && stat.Total > 0 && stat.Current == 0) {
                // A living actor's share rounds down to 0 only when the maximum falls far (a debuff): it keeps 1, as
                // a rebuild is no hit and kills no one.
                stat.Current = 1;
            } else if (dead && attribute == BasicAttribute.Health) {
                stat.Current = 0;
            } else if (levelUp && !dead && attribute is BasicAttribute.Health or BasicAttribute.Spirit) {
                stat.Current = stat.Total;
            }
        }
        foreach (Stat stat in specialValues.Values) {
            stat.EndHold();
        }
    }

    /// <summary>
    /// Safely clear stats.
    /// </summary>
    private void ClearStats() {
        foreach (Stat stat in basicValues.Values) {
            stat.AddBase(-stat.Base);
            stat.AddTotal(-stat.Total);
            stat.AddRate(-stat.Rate);
        }
        foreach (Stat stat in specialValues.Values) {
            stat.AddBase(-stat.Base);
            stat.AddTotal(-stat.Total);
            stat.AddRate(-stat.Rate);
        }
    }
}

public sealed class Stat {
    public const int TOTAL = 3;

    public long Total { get; set; }
    public long Base { get; set; }
    public long Current { get; set; }
    public float Rate { get; set; }
    // A special attribute's stat: its Total is the plain sum of its lines and may go below 0, as the client sums them
    // (CEquipment::GetAbilityTotal adds the items', the buffs', the game rule's and the set items' lines with no floor;
    // a use clamps where it needs to). Its Current follows its Total. A basic stat stops at 0 on each add.
    public bool Signed { get; init; }
    // Set during a Stats rebuild: Base and Total change, Current waits for Stats.EndReset.
    internal bool Held;
    private long heldTotal;

    public Stat(long value) : this(value, value, value) { }

    public Stat(long total, long @base, long current) {
        Total = total;
        Base = @base;
        Current = current;
    }

    public void AddBase(long amount) {
        if (Signed) {
            Base += amount;
            AddTotal(amount);
            return;
        }
        Total = Math.Max(0, Total + amount);
        Base = Math.Max(0, Base + amount);
        if (!Held) {
            Current = Math.Max(0, Current + amount);
        }
    }

    public void AddTotal(long amount) {
        if (Signed) {
            Total += amount;
            Current = Total;
            return;
        }
        Total = Math.Max(0, Total + amount);
        if (!Held) {
            Current = Math.Max(0, Current + amount);
        }
    }

    public void AddTotal(BasicOption option) {
        AddTotal(option.Value);
        // Rate += option.Value;
    }

    public void AddTotal(SpecialOption option) {
        // A special rate is kept in thousandths, which is what Multiplier() divides by.
        AddTotal(option.Value);
    }

    public void AddRate(float rate) {
        Rate += rate;
    }

    internal void Hold() {
        // A stat held twice (a rebuild that did not end) keeps the Total it had when the first one started.
        if (!Held) {
            heldTotal = Total;
            Held = true;
        }
    }

    internal void EndHold() {
        if (Held && Signed) {
            Current = Total;
            Held = false;
        } else if (Held) {
            // A stat that had no Total before (new to this actor) starts full.
            Current = heldTotal <= 0 ? Total : Math.Clamp((long) ((Int128) Current * Total / heldTotal), 0, Total);
            Held = false;
        }
    }

    public void Add(long amount) {
        if (Signed) {
            return;
        }
        // During a rebuild Total is not final yet, so it is no cap.
        Current = Held ? Math.Max(0, Current + amount) : Math.Clamp(Current + amount, 0, Total);
    }

    public long this[int i] {
        get {
            return i switch {
                0 => Total,
                1 => Base,
                _ => Current,
            };
        }
    }

    public double Multiplier() => Total / 1000.0;

    public override string ToString() => $"<{Total}|{Base}|{Current}>";
}
