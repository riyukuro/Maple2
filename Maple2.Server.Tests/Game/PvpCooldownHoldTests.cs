using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// A PvP-zone entry ends the flagged cooldowns there, but the town ones are held and come back on the way out (any
// map of another type), unless they ran out meanwhile or a use inside ends later. 10000001 is a flagged skill,
// 10000002 an unflagged one; ticks are milliseconds.
public class PvpCooldownHoldTests {
    private const int Flagged = 10000001;
    private const int Unflagged = 10000002;
    private const long CharacterId = 7;

    private static SkillCooldown Cooldown(int id, long endTick) => new(id, 1) { EndTick = endTick };

    // The order of ConfigManager.LoadSkillCooldowns: hold or put back, then the clear.
    private static void EnterMap(PvpCooldownHold<SkillCooldown> hold, IDictionary<int, SkillCooldown> cooldowns, MapType type, long tick) {
        List<int> cleared = SkillCastRules.PvpZoneClears(type, cooldowns.Keys, id => id == Flagged);
        hold.EnterMap(type == MapType.Pvp, cooldowns, cleared, tick);
        foreach (int id in cleared) {
            cooldowns.Remove(id);
        }
    }

    private static (PvpCooldownHold<SkillCooldown> Hold, Dictionary<int, SkillCooldown> Cooldowns) InArena() {
        var hold = new PvpCooldownHold<SkillCooldown>(ConfigManager.SettledEnd);
        var cooldowns = new Dictionary<int, SkillCooldown> {
            [Flagged] = Cooldown(Flagged, 30000),
            [Unflagged] = Cooldown(Unflagged, 20000),
        };
        EnterMap(hold, cooldowns, MapType.Pvp, 3000);
        return (hold, cooldowns);
    }

    [Test]
    public void ArenaEntry_HoldsTheTownCooldown_AndExitPutsItBack() {
        (PvpCooldownHold<SkillCooldown> hold, Dictionary<int, SkillCooldown> cooldowns) = InArena();
        Assert.That(cooldowns.ContainsKey(Flagged), Is.False);
        Assert.That(hold.InStay, Is.True);

        EnterMap(hold, cooldowns, MapType.None, 10000);
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(30000));
        Assert.That(hold.InStay, Is.False);
        Assert.That(hold.Held, Is.Empty);
    }

    [Test]
    public void ExpiredInside_IsNotPutBack() {
        (PvpCooldownHold<SkillCooldown> hold, Dictionary<int, SkillCooldown> cooldowns) = InArena();
        EnterMap(hold, cooldowns, MapType.Dungeon, 31000);
        Assert.That(cooldowns.ContainsKey(Flagged), Is.False);
    }

    [TestCase(50000, 50000)]
    [TestCase(20000, 30000)]
    public void UsedInside_TheLaterEndIsKept(long insideEnd, long expectedEnd) {
        (PvpCooldownHold<SkillCooldown> hold, Dictionary<int, SkillCooldown> cooldowns) = InArena();
        cooldowns[Flagged] = Cooldown(Flagged, insideEnd);
        EnterMap(hold, cooldowns, MapType.None, 10000);
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(expectedEnd));
    }

    [Test]
    public void PvpToPvp_KeepsTheFirstHold() {
        (PvpCooldownHold<SkillCooldown> hold, Dictionary<int, SkillCooldown> cooldowns) = InArena();
        cooldowns[Flagged] = Cooldown(Flagged, 12000);
        EnterMap(hold, cooldowns, MapType.Pvp, 5000);
        Assert.That(hold.Held[Flagged].EndTick, Is.EqualTo(30000));

        EnterMap(hold, cooldowns, MapType.None, 6000);
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(30000));
    }

    [Test]
    public void Unflagged_IsUntouched() {
        (PvpCooldownHold<SkillCooldown> hold, Dictionary<int, SkillCooldown> cooldowns) = InArena();
        SkillCooldown unflagged = cooldowns[Unflagged];
        Assert.That(hold.Held.ContainsKey(Unflagged), Is.False);

        EnterMap(hold, cooldowns, MapType.None, 10000);
        Assert.That(cooldowns[Unflagged], Is.SameAs(unflagged));
        Assert.That(unflagged.EndTick, Is.EqualTo(20000));
    }

    [Test]
    public void HeldRecharge_SettlesItsUsesOnTheWayOut() {
        var hold = new PvpCooldownHold<SkillCooldown>(ConfigManager.SettledEnd);
        var cooldowns = new Dictionary<int, SkillCooldown> {
            [Flagged] = new(Flagged, 1) { EndTick = 10000, Charges = 2, RechargeMs = 10000 },
        };
        EnterMap(hold, cooldowns, MapType.Pvp, 3000);
        EnterMap(hold, cooldowns, MapType.None, 12000);
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(20000));
        Assert.That(cooldowns[Flagged].Charges, Is.EqualTo(1));
    }

    [Test]
    public void EffectCooldowns_AreHeldTheSameWay() {
        var hold = new PvpCooldownHold<long>((endTick, _) => endTick);
        var cooldowns = new ConcurrentDictionary<int, long> { [1] = 30000, [2] = 20000 };
        hold.EnterMap(true, cooldowns, [1], 3000);
        cooldowns.TryRemove(1, out _);

        Assert.That(hold.EnterMap(false, cooldowns, [], 10000), Is.EqualTo(new[] { 1 }));
        Assert.That(cooldowns[1], Is.EqualTo(30000));
        Assert.That(cooldowns[2], Is.EqualTo(20000));
    }

    // A logout inside the arena: the hold rides the World's player config cache to the next login.
    private static PlayerConfigLookUp LogoutInArena(bool usesGameTime) {
        (PvpCooldownHold<SkillCooldown> hold, _) = InArena();
        var lookUp = new PlayerConfigLookUp(null!, (_, _) => usesGameTime);
        List<SkillCooldownInfo> held = hold.Held.Values.Select(cooldown => ConfigManager.ToCache(cooldown, 5000, 1000)).ToList();
        lookUp.Save([], [], new DeathInfo(), CharacterId, held, hold.InStay);
        return lookUp;
    }

    private static Dictionary<int, SkillCooldown> LoginToTown(PlayerConfigLookUp lookUp, long epochSeconds, long tick) {
        (List<SkillCooldownInfo> held, bool stay) = lookUp.RetrievePvpHold(CharacterId, epochSeconds);
        var hold = new PvpCooldownHold<SkillCooldown>(ConfigManager.SettledEnd);
        hold.Load(held.Select(info => ConfigManager.FromCache(info, tick)), cooldown => cooldown.SkillId, stay);
        var cooldowns = new Dictionary<int, SkillCooldown>();
        EnterMap(hold, cooldowns, MapType.None, tick);
        return cooldowns;
    }

    [Test]
    public void LogoutInArena_LoginInTown_PutsTheHoldBack() {
        // 25 s left at the logout, 10 s away.
        Dictionary<int, SkillCooldown> cooldowns = LoginToTown(LogoutInArena(false), 1010, 100000);
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(115000));
    }

    [Test]
    public void LogoutInArena_LoginInTown_DropsAnExpiredHold() {
        Dictionary<int, SkillCooldown> cooldowns = LoginToTown(LogoutInArena(false), 1030, 100000);
        Assert.That(cooldowns, Is.Empty);
    }

    [Test]
    public void LogoutInArena_InGameTimeCooldown_DoesNotRunAway() {
        Dictionary<int, SkillCooldown> cooldowns = LoginToTown(LogoutInArena(true), 1030, 100000);
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(125000));
    }

    [Test]
    public void HoldLostWithTheCache_RestoresNothing() {
        var lookUp = new PlayerConfigLookUp(null!, (_, _) => false);
        Assert.That(lookUp.RetrievePvpHold(CharacterId, 1010), Is.EqualTo((new List<SkillCooldownInfo>(), false)));
        Assert.That(LoginToTown(lookUp, 1010, 100000), Is.Empty);
    }

    [Test]
    public void ArenaRoundTrip_PutsTheLastTownCastBack_SoItsCooldownIsChecked() {
        // A job skill's cooldown is checked on its last cast (ConfigManager.CastTimingRefusal); the clear removes it.
        var hold = new PvpCooldownHold<SkillCastRules.LastCast>(ConfigManager.CastHoldEnd);
        var lastCasts = new Dictionary<int, SkillCastRules.LastCast> {
            [Flagged] = new(Flagged, 0, 30000, 1000, 1000),
        };
        List<int> cleared = SkillCastRules.PvpZoneClears(MapType.Pvp, lastCasts.Keys, id => id == Flagged);
        hold.EnterMap(true, lastCasts, cleared, 3000);
        foreach (int id in cleared) {
            lastCasts.Remove(id);
        }
        Assert.That(SkillCastRules.CooldownShortfall(lastCasts.Values, Flagged, 0, 10000, 10000), Is.Zero);

        hold.EnterMap(false, lastCasts, [], 10000);
        Assert.That(SkillCastRules.CooldownShortfall(lastCasts.Values, Flagged, 0, 10000, 10000), Is.GreaterThan(0));
    }

    [Test]
    public void RechargeSkill_KeepsTheRecordWithMoreUsesOut() {
        // 2 uses out in town (the next back at 30 s); one use inside ends later but has 1 out.
        PvpCooldownHold<SkillCooldown> hold = ConfigManager.NewPvpHold();
        var cooldowns = new Dictionary<int, SkillCooldown> {
            [Flagged] = new(Flagged, 1) { EndTick = 30000, Charges = 2, RechargeMs = 20000 },
        };
        EnterMap(hold, cooldowns, MapType.Pvp, 3000);
        cooldowns[Flagged] = new SkillCooldown(Flagged, 1) { EndTick = 40000, Charges = 1, RechargeMs = 20000 };

        EnterMap(hold, cooldowns, MapType.None, 10000);
        Assert.That(cooldowns[Flagged].Charges, Is.EqualTo(2));
        Assert.That(cooldowns[Flagged].EndTick, Is.EqualTo(30000));
    }

    [Test]
    public void SaveOnAnotherThread_ReadsTheHoldWhileMapEntriesChangeIt() {
        // The disconnect save (GameSession.SaveCacheConfig) reads a copy while the session thread enters maps.
        var hold = new PvpCooldownHold<long>((endTick, _) => endTick);
        var cooldowns = new Dictionary<int, long>();
        int[] ids = Enumerable.Range(1, 64).ToArray();
        using var stop = new CancellationTokenSource();
        Task reader = Task.Run(() => {
            while (!stop.IsCancellationRequested) {
                (Dictionary<int, long> held, bool _) = hold.Snapshot();
                _ = held.Values.Sum();
            }
        });
        for (int i = 0; i < 2000; i++) {
            foreach (int id in ids) {
                cooldowns[id] = 1000000;
            }
            hold.EnterMap(true, cooldowns, ids, i);
            hold.EnterMap(false, new Dictionary<int, long>(), [], i);
        }
        stop.Cancel();
        Assert.DoesNotThrowAsync(async () => await reader);
    }
}
