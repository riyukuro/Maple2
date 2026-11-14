using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game.Pvp;

// The rank duel field glue (RankDuelField) in a test field that holds both duelists: the round and fight packets,
// the duelists as each other's hostile skill targets, and a result that moves both records (rating, tokens, exp).
public class RankDuelFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly RankDuelGradeTable Grades = new(new Dictionary<int, RankDuelGradeTable.Grade> {
        [1] = new(1, 1000, 10, 10, 600, 30, -10),
        [2] = new(2, 1060, 20, 15, 1000, 30, -15),
    });

    private sealed class Duel {
        public required TestGameSession A;
        public required TestGameSession B;
        public required TestGameSession Bystander;
        public required FieldManager Field;
        public readonly List<int> Ended = [];
        public FieldPlayer PlayerA => A.Session.Player;
        public FieldPlayer PlayerB => B.Session.Player;
    }

    private static void Tables(TestGameSession test, int season) {
        var seasons = new Dictionary<int, SeasonDataTable.Entry> {
            [season] = new(season, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1), [1000, 1060, 1300, 1800, 2200, 2800, 3700]),
        };
        var none = new Dictionary<int, SeasonDataTable.Entry>();
        test.SetTable(new SeasonDataTable(none, none, none, none, none, none, seasons, none, none));
        test.SetTable(Grades);
        // The valor token cap the currency keeps to.
        test.SetServerTable(new ConstantsTable(HonorTokenMax: 30000));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.rankDuelWin] = new(1, 6),
            [ExpType.rankDuelLose] = new(1, 2),
        }));
        test.SetTable(new ExpTable(
            new Dictionary<int, IReadOnlyDictionary<int, long>> { [1] = new Dictionary<int, long> { [1] = 100 } },
            new Dictionary<int, long> { [1] = 1_000_000 }));
    }

    // A, B and a bystander (C) in A's field, which is made a rank duel field for A and B.
    private static Duel Start(int season = 90) {
        var a = new TestGameSession();
        a.Start();
        var b = new TestGameSession(a.Options);
        b.Start();
        var c = new TestGameSession(a.Options);
        c.Start();
        foreach (TestGameSession test in new[] { a, b, c }) {
            Tables(test, season);
        }

        FieldManager field = a.Session.Field!;
        typeof(FieldManager).GetField("<GameStorage>k__BackingField", Any)!.SetValue(field, a.Storage);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        int objectId = 2;
        foreach (TestGameSession other in new[] { b, c }) {
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, objectId);
            field.Players[objectId++] = other.Session.Player;
            other.Session.Field = field;
        }

        var duel = new Duel { A = a, B = b, Bystander = c, Field = field };
        field.StartRankDuel(7, a.CharacterId, b.CharacterId, duel.Ended.Add);
        return duel;
    }

    private static List<string> Pvp(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Pvp).Select(Convert.ToHexString).ToList();
    }

    private static void Fight(Duel duel) {
        duel.Field.RankDuel!.Enter(duel.PlayerA);
        duel.Field.RankDuel.Enter(duel.PlayerB);
        duel.Field.RankDuel.Update(duel.Field.FieldTick + RankDuelMatchState.CountdownMs);
    }

    // The hostile pool of a player caster around the origin (where the test players stand).
    private static List<IActor> Hostile(Duel duel, FieldPlayer caster) {
        var prisms = new[] { new Prism(new Circle(Vector2.Zero, 500), -100, 300) };
        MethodInfo pool = typeof(FieldManager).GetMethod("GetTargetPool", Any)!;
        var targets = (IEnumerable<IActor>) pool.Invoke(duel.Field, [caster, prisms, ApplyTargetType.Hostile, 10, new List<IActor> { caster }])!;
        return targets.ToList();
    }

    [Test]
    public void BothIn_RoundThenFight_ToBothDuelists() {
        Duel duel = Start();
        duel.A.TakeSent();
        duel.B.TakeSent();
        duel.Bystander.TakeSent();
        duel.Field.RankDuel!.Enter(duel.PlayerA);
        Assert.That(Pvp(duel.A), Is.Empty);
        duel.Field.RankDuel.Enter(duel.PlayerB);
        Assert.That(Pvp(duel.A), Is.EqualTo(new[] { "B600" + "1B" + "C4090000" + "01000000" }));
        Assert.That(Pvp(duel.B), Is.EqualTo(new[] { "B600" + "1B" + "C4090000" + "01000000" }));

        duel.Field.RankDuel.Update(duel.Field.FieldTick + RankDuelMatchState.CountdownMs);
        // The fight intro, then each duelist's PvP point (A 101, B 102: the client's enemies; 1 and 2 are its own teams), seen by the whole field.
        string points = "B600" + "64" + Int(duel.PlayerA.ObjectId) + "65000000" + "00" + "00" + "|" + "B600" + "64" + Int(duel.PlayerB.ObjectId) + "66000000" + "00" + "00";
        Assert.That(string.Join("|", Pvp(duel.A)), Is.EqualTo("B600" + "1A" + "DC050000" + "|" + points));
        Assert.That(string.Join("|", Pvp(duel.B)), Is.EqualTo("B600" + "1A" + "DC050000" + "|" + points));
        Assert.That(string.Join("|", Pvp(duel.Bystander)), Is.EqualTo(points));
    }

    // During the fight each duelist's hostile skills reach the other, never the bystander; before it, nobody.
    [Test]
    public void Fight_DuelistsAreEachOthersTargets() {
        Duel duel = Start();
        duel.Field.RankDuel!.Enter(duel.PlayerA);
        duel.Field.RankDuel.Enter(duel.PlayerB);
        Assert.That(Hostile(duel, duel.PlayerA), Is.Empty);

        duel.Field.RankDuel.Update(duel.Field.FieldTick + RankDuelMatchState.CountdownMs);
        Assert.That(Hostile(duel, duel.PlayerA), Is.EqualTo(new IActor[] { duel.PlayerB }));
        Assert.That(Hostile(duel, duel.PlayerB), Is.EqualTo(new IActor[] { duel.PlayerA }));
        Assert.That(Hostile(duel, duel.Bystander.Session.Player), Is.Empty);
    }

    // B dies: A wins. Both records move by their grade (grade 1: +30 and -10, 10 tokens each), the tokens and the
    // exp reach both duelists, the result carries the new ratings, Pvp 15 and the new standing follow, and the
    // world is told the match ended.
    [Test]
    public void Death_WinnerAndLoserRecords() {
        Duel duel = Start();
        Fight(duel);
        duel.A.TakeSent();
        duel.B.TakeSent();
        long tokensA = duel.A.Session.Currency[CurrencyType.ValorToken];
        long tokensB = duel.B.Session.Currency[CurrencyType.ValorToken];
        long expA = duel.A.Session.Player.Value.Character.Exp;
        long expB = duel.B.Session.Player.Value.Character.Exp;
        int jobA = (int) duel.PlayerA.Value.Character.Job.Code();
        int jobB = (int) duel.PlayerB.Value.Character.Job.Code();

        // B took health from A first, so the win is no perfect one.
        duel.Field.RankDuel!.Damage(duel.PlayerB, duel.PlayerA);
        duel.Field.RankDuel!.Death(duel.PlayerB);

        using (GameStorage.Request db = duel.A.Storage.Context()) {
            RankDuelRecord a = db.GetRankDuelRecord(duel.A.CharacterId);
            RankDuelRecord b = db.GetRankDuelRecord(duel.B.CharacterId);
            Assert.That((a.Season, a.Rating, a.BestRating, a.WeeklyReward), Is.EqualTo((90, 1030, 1030, 10)));
            Assert.That((b.Season, b.Rating, b.BestRating, b.WeeklyReward), Is.EqualTo((90, 990, 1000, 10)));
            Assert.That((a.Jobs[jobB].Wins, a.Jobs[jobB].Losses), Is.EqualTo((1, 0)));
            Assert.That((b.Jobs[jobA].Wins, b.Jobs[jobA].Losses), Is.EqualTo((0, 1)));
        }
        Assert.That(duel.A.Session.Currency[CurrencyType.ValorToken], Is.EqualTo(tokensA + 10));
        Assert.That(duel.B.Session.Currency[CurrencyType.ValorToken], Is.EqualTo(tokensB + 10));
        // exp: the curve's 100 at level 1 times the factor (6 for a win, 2 for a loss).
        Assert.That(duel.A.Session.Player.Value.Character.Exp, Is.EqualTo(expA + 600));
        Assert.That(duel.B.Session.Player.Value.Character.Exp, Is.EqualTo(expB + 200));

        List<string> sentA = Pvp(duel.A);
        Assert.That(sentA[0], Is.EqualTo("B600" + "1C" + "B80B0000"));
        Assert.That(sentA[1], Does.StartWith("B600" + "12" + Long(duel.A.CharacterId)));
        Assert.That(sentA[1], Does.Contain(Long(duel.A.CharacterId) + "01000000" + "06040000" + "1E000000" + "0A000000"));
        Assert.That(sentA[1], Does.Contain(Long(duel.B.CharacterId) + "00000000" + "DE030000" + "F6FFFFFF" + "0A000000"));
        Assert.That(sentA.Skip(2), Is.EqualTo(new[] {
            "B600" + "16" + "5A000000" + "06040000" + "06040000" + "0A000000",
            "B600" + "17" + "01000000" + Int(jobB) + "01000000" + "00000000",
            "B600" + "64" + Int(duel.PlayerA.ObjectId) + "00000000" + "0000",
            "B600" + "64" + Int(duel.PlayerB.ObjectId) + "00000000" + "0000",
            "B600" + "0F",
        }));
        Assert.That(Pvp(duel.B)[0], Is.EqualTo("B600" + "1D" + "B80B0000"));

        System.Threading.SpinWait.SpinUntil(() => duel.Ended.Count > 0, 2000);
        Assert.That(duel.Ended, Is.EqualTo(new[] { 7 }));
    }

    // A win where the loser's hits took no health from the winner: the winner gets the perfect intro (Pvp 31) in
    // place of the win intro, the loser its lose intro; a hit of the winner on the loser does not matter.
    [Test]
    public void Death_UntouchedWinner_PerfectIntro() {
        Duel duel = Start();
        Fight(duel);
        duel.A.TakeSent();
        duel.B.TakeSent();

        duel.Field.RankDuel!.Damage(duel.PlayerA, duel.PlayerB);
        duel.Field.RankDuel!.Death(duel.PlayerB);

        Assert.That(Pvp(duel.A)[0], Is.EqualTo("B600" + "1F" + "B80B0000"));
        Assert.That(Pvp(duel.B)[0], Is.EqualTo("B600" + "1D" + "B80B0000"));
    }

    // A duelist who left before the result loses the rating but gets no tokens; the one in the field gets its own.
    [Test]
    public void Leaver_LosesRatingWithoutTokens() {
        Duel duel = Start();
        Fight(duel);
        duel.Field.Players.TryRemove(duel.PlayerB.ObjectId, out _);
        duel.Field.RankDuel!.Leave(duel.PlayerB);

        using GameStorage.Request db = duel.A.Storage.Context();
        RankDuelRecord b = db.GetRankDuelRecord(duel.B.CharacterId);
        Assert.That((b.Rating, b.WeeklyReward), Is.EqualTo((990, 0)));
        Assert.That(db.GetRankDuelRecord(duel.A.CharacterId).WeeklyReward, Is.EqualTo(10));
    }

    // A record of an earlier season starts over before the result moves it.
    [Test]
    public void OldSeason_StartsOver() {
        Duel duel = Start(season: 91);
        using (GameStorage.Request db = duel.A.Storage.Context()) {
            var old = new RankDuelRecord { Season = 90, Rating = 2000, BestRating = 2100, WeeklyReward = 50 };
            old.Jobs[10] = new RankDuelJobRecord { Wins = 3 };
            db.SaveRankDuelRecord(duel.A.CharacterId, old);
        }
        Fight(duel);
        duel.Field.RankDuel!.Death(duel.PlayerB);

        using GameStorage.Request check = duel.A.Storage.Context();
        RankDuelRecord a = check.GetRankDuelRecord(duel.A.CharacterId);
        Assert.That((a.Season, a.Rating, a.BestRating, a.WeeklyReward), Is.EqualTo((91, 1030, 1030, 60)));
        Assert.That(a.Jobs.ContainsKey(10), Is.False);
    }

    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Int(int value) => Convert.ToHexString(BitConverter.GetBytes(value));

    // Both duelists come after the result: each gets Pvp 15 once and one send-out (queued for the field's next
    // tick), and the exit leaves them alone (its own send-out would be a second one).
    [Test]
    public void LateArrivals_DismissedOnceAndSkippedByTheExit() {
        Duel duel = Start();
        long start = Environment.TickCount64;
        duel.Field.RankDuel!.Update(start + RankDuelMatchState.JoinMs * 100);
        Assert.That(duel.Field.RankDuel.Phase, Is.EqualTo(RankDuelPhase.Ended));
        duel.A.TakeSent();
        duel.B.TakeSent();
        int queued = duel.Field.Scheduler.Queued;

        duel.Field.RankDuel.Enter(duel.PlayerA);
        duel.Field.RankDuel.Enter(duel.PlayerB);
        duel.Field.RankDuel.Enter(duel.PlayerA);
        Assert.That(Pvp(duel.A), Is.EqualTo(new[] { "B600" + "0F" }));
        Assert.That(Pvp(duel.B), Is.EqualTo(new[] { "B600" + "0F" }));
        Assert.That(duel.Field.Scheduler.Queued, Is.EqualTo(queued + 2));

        duel.Field.RankDuel.Update(start + RankDuelMatchState.JoinMs * 100 + RankDuelMatchState.ResultMs);
        Assert.That(duel.Field.RankDuel.Phase, Is.EqualTo(RankDuelPhase.Closed));
        Assert.That(duel.Field.RankDuel.Runs, Is.False);
        Assert.That(duel.A.TakeSent(), Is.Empty);
        Assert.That(duel.B.TakeSent(), Is.Empty);
    }

    // Both records go in one save: a record of a character that is gone fails it, and the other is not written.
    [Test]
    public void Records_SavedTogetherOrNotAtAll() {
        Duel duel = Start();
        using GameStorage.Request db = duel.A.Storage.Context();
        var records = new Dictionary<long, RankDuelRecord> {
            [duel.A.CharacterId] = new() { Season = 90, Rating = 1500 },
            [987654321] = new() { Season = 90, Rating = 900 },
        };
        Assert.That(db.SaveRankDuelRecords(records), Is.False);
        Assert.That(db.GetRankDuelRecord(duel.A.CharacterId).Rating, Is.EqualTo(RankDuelRecord.StartRating));

        records.Remove(987654321);
        records[duel.B.CharacterId] = new RankDuelRecord { Season = 90, Rating = 900 };
        Assert.That(db.SaveRankDuelRecords(records), Is.True);
        Assert.That(db.GetRankDuelRecord(duel.A.CharacterId).Rating, Is.EqualTo(1500));
        Assert.That(db.GetRankDuelRecord(duel.B.CharacterId).Rating, Is.EqualTo(900));
    }

    // An onlooker (the observe offer's join) entering during the fight becomes an onlooker (Pvp 0x13 false) and gets the
    // duel's observer dialog (Trigger dialog kind 5) with both duelists; at the result the dialog closes and 0x13 true
    // ends it.
    [Test]
    public void Observer_DuringTheFight() {
        Duel duel = Start();
        Fight(duel);
        duel.Bystander.TakeSent();
        // A rating changed after the fight started: the dialog keeps the one read at its start.
        using (GameStorage.Request db = duel.A.Storage.Context()) {
            db.SaveRankDuelRecord(duel.B.CharacterId, new RankDuelRecord { Season = 90, Rating = 1500, BestRating = 1500 });
        }
        duel.Field.RankDuel!.Enter(duel.Bystander.Session.Player);
        List<byte[]> entered = duel.Bystander.TakeSent();
        Assert.That(Convert.ToHexString(entered[0]), Is.EqualTo("B600" + "13" + "00"));
        // The duelists' points, so the client follows their health into the dialog.
        Assert.That(Convert.ToHexString(entered[1]), Is.EqualTo("B600" + "64" + Int(duel.PlayerA.ObjectId) + "65000000" + "0000"));
        Assert.That(Convert.ToHexString(entered[2]), Is.EqualTo("B600" + "64" + Int(duel.PlayerB.ObjectId) + "66000000" + "0000"));
        string dialog = Convert.ToHexString(entered[3]);
        Assert.That(dialog, Does.Contain(Long(duel.B.CharacterId) + "00000000" + "00000000" + "00" + "E8030000"));
        Assert.That(dialog[..8], Is.EqualTo("4E00" + "0F" + "05"));
        Assert.That(dialog, Does.Contain(Long(duel.A.CharacterId)));
        Assert.That(dialog, Does.Contain(Long(duel.B.CharacterId)));

        duel.Field.RankDuel.Death(duel.PlayerB);
        List<string> after = duel.Bystander.TakeSent().Select(Convert.ToHexString).ToList();
        Assert.That(after, Does.Contain("B600" + "13" + "01"));
        Assert.That(after.Any(packet => packet.StartsWith("4E000F05") && packet.EndsWith("01" + new string('0', 46 * 2))), Is.True);
    }

    // An onlooker takes no part: its friendly and hostile skills reach no player, and a duelist's friendly skills do
    // not reach it.
    [Test]
    public void Onlooker_IsNoTarget() {
        Duel duel = Start();
        Fight(duel);
        FieldPlayer onlooker = duel.Bystander.Session.Player;
        duel.Field.RankDuel!.Enter(onlooker);
        Assert.That(Pool(duel, onlooker, ApplyTargetType.Friendly), Is.Empty);
        Assert.That(Pool(duel, onlooker, ApplyTargetType.Hostile), Is.Empty);
        Assert.That(Pool(duel, duel.PlayerA, ApplyTargetType.Friendly), Does.Not.Contain(onlooker));
        Assert.That(Pool(duel, duel.PlayerA, ApplyTargetType.Friendly), Does.Contain(duel.PlayerB));
    }

    private static List<IActor> Pool(Duel duel, FieldPlayer caster, ApplyTargetType type) {
        var prisms = new[] { new Prism(new Circle(Vector2.Zero, 500), -100, 300) };
        MethodInfo pool = typeof(FieldManager).GetMethod("GetTargetPool", Any)!;
        return ((IEnumerable<IActor>) pool.Invoke(duel.Field, [caster, prisms, type, 10, new List<IActor> { caster }])!).ToList();
    }
}
