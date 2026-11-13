using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MedalType = Maple2.Model.Enum.MedalType;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.Pvp;

// The restored client's Maple Survival match: the packets byte for byte as its readers take them, the
// queue, the game and the storm, and the 0xC2 requests.
public class MapleSurvivalTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Float(float value) => Convert.ToHexString(BitConverter.GetBytes(value));

    // Survival 0x119 (op bytes 1901), SurvivalEvent 0x116 (1601).
    [Test]
    public void SurvivalLayouts() {
        Assert.That(Hex(MapleSurvivalPacket.Matching()), Is.EqualTo("1901" + "00"));
        Assert.That(Hex(MapleSurvivalPacket.Idle()), Is.EqualTo("1901" + "01"));
        Assert.That(Hex(MapleSurvivalPacket.Matched(0x1122, true)), Is.EqualTo("1901" + "02" + Long(0x1122) + "01"));
        Assert.That(Hex(MapleSurvivalPacket.Reinvite(5, false)), Is.EqualTo("1901" + "06" + Long(5) + "00"));
        Assert.That(Hex(MapleSurvivalPacket.GameStart()), Is.EqualTo("1901" + "13"));
        Assert.That(Hex(MapleSurvivalPacket.GameFinish(true, [7, 8])), Is.EqualTo("1901" + "14" + "01" + "02000000" + Long(7) + Long(8)));
        Assert.That(Hex(MapleSurvivalPacket.KillFeed("A", "B", 0, 3)), Is.EqualTo("1901" + "19" + "00" + "0100" + "4100" + "0100" + "4200" + "00" + "03000000"));
        Assert.That(Hex(MapleSurvivalPacket.AliveCount(19)), Is.EqualTo("1901" + "1A" + "13000000"));
        Assert.That(Hex(MapleSurvivalPacket.InStorm()), Is.EqualTo("1901" + "1D"));
        // 0x11: 16 ints, 4 floats, the item count.
        string result = Hex(MapleSurvivalPacket.Result([1, 0, 2, 20]));
        Assert.That(result, Is.EqualTo("1901" + "11" + "01000000" + "00000000" + "02000000" + "14000000" + string.Concat(Enumerable.Repeat("00000000", 12))
            + string.Concat(Enumerable.Repeat("00000000", 4)) + "00"));
    }

    [Test]
    public void StormLayouts() {
        var center = new Vector3(1, 2, 3);
        Assert.That(Hex(MapleSurvivalPacket.AddZone(1, 5000, 2, center, 4000, center)),
            Is.EqualTo("1601" + "00" + "0100" + "8813" + "0200" + Float(1) + Float(2) + Float(3) + "A00F" + Float(1) + Float(2) + Float(3)));
        // Mode 1: the zone's new radius (a float), then its center.
        Assert.That(Hex(MapleSurvivalPacket.UpdateZone(1, 4300f, center)), Is.EqualTo("1601" + "01" + "0100" + Float(4300) + Float(1) + Float(2) + Float(3)));
        Assert.That(Hex(MapleSurvivalPacket.Countdown(60)), Is.EqualTo("1601" + "06" + "3C00"));
        Assert.That(Hex(MapleSurvivalPacket.RemoveZone(1)), Is.EqualTo("1601" + "02" + "0100"));
        Assert.That(Hex(MapleSurvivalPacket.ClearZones()), Is.EqualTo("1601" + "03"));
        Assert.That(Hex(MapleSurvivalPacket.ReduceReady(10)), Is.EqualTo("1601" + "04" + "0A00"));
        Assert.That(Hex(MapleSurvivalPacket.ReduceStart()), Is.EqualTo("1601" + "05"));
    }

    // A survival match takes its full count only: no gathering of fewer; a no on the box lets the character go.
    [Test]
    public void Queue_FullCountAndDecline() {
        var queue = new ArenaQueue();
        queue.Register(1, MapleSurvivalRules.Arena, 3, 0, 3, out _);
        queue.Register(2, MapleSurvivalRules.Arena, 3, 0, 3, out _);
        Assert.That(queue.Pair(ArenaQueue.GatherMs * 10), Is.Empty);
        queue.Register(3, MapleSurvivalRules.Arena, 3, 1, 3, out List<ArenaMatch> made);
        ArenaMatch match = made.Single();
        Assert.That(match.Characters, Is.EqualTo(new long[] { 1, 2, 3 }));
        queue.SetRoom(match.Id, 1, MapleSurvivalRules.MapId, 5);
        Assert.That(queue.Decline(2), Is.EqualTo(MapleSurvivalRules.Arena));
        Assert.That(queue.MatchOf(2), Is.Null);
        Assert.That(queue.Decline(2), Is.EqualTo(0));
        queue.Join(1, out _);
        Assert.That(queue.Decline(1), Is.EqualTo(0));
    }

    // The game: the fallen ranked by those left, a kill credited to a recent hitter, the last one the winner.
    [Test]
    public void Game_FallKillsWinner() {
        var game = new SurvivalGameState([1, 2, 3, 4]);
        Assert.That(game.Alive, Is.EqualTo(4));
        Assert.That(game.Hostile(1, 2), Is.True);
        game.Hit(1, 2, 100);
        Assert.That(game.Fall(2, 200, out long killer), Is.True);
        Assert.That((killer, game.Get(2)!.Rank, game.Get(1)!.Kills), Is.EqualTo((1L, 4, 1)));
        Assert.That(game.Fall(2, 300, out _), Is.False);
        Assert.That(game.Hostile(1, 2), Is.False);
        // A hit too long ago is no kill.
        game.Hit(1, 3, 1_000);
        game.Fall(3, 1_000 + SurvivalGameState.KillCreditMs + 1, out killer);
        Assert.That((killer, game.Get(3)!.Rank), Is.EqualTo((0L, 3)));
        game.Fall(4, 20_000, out _);
        Assert.That((game.Ended, game.Winner, game.Get(1)!.Rank, game.Get(4)!.Rank), Is.EqualTo((true, 1L, 1, 2)));
        Assert.That(game.Fallen, Is.EqualTo(new long[] { 2, 3, 4 }));
        Assert.That(game.Hostile(1, 1), Is.False);
    }

    // Squads: members are no enemies; a squad's rank is set when its last member falls (the squads left, plus one) and
    // shared by its members; the game ends with one squad left, whose members (fallen ones too) rank 1. A squad never
    // splits in the queue, and a squad that would take a match past its count still goes in whole.
    [Test]
    public void Squads_HostilityRanksAndQueue() {
        var units = new Dictionary<long, long> { [1] = 1, [2] = 1, [3] = 3, [4] = 4 };
        var game = new SurvivalGameState([1, 2, 3, 4], id => units[id]);
        Assert.That(game.Hostile(1, 2), Is.False);
        Assert.That(game.Hostile(1, 3), Is.True);
        game.Fall(2, 0, out _);
        Assert.That(game.Get(2)!.Rank, Is.EqualTo(0));
        game.Fall(4, 0, out _);
        Assert.That(game.Get(4)!.Rank, Is.EqualTo(3));
        Assert.That(game.Ended, Is.False);
        game.Fall(3, 0, out _);
        Assert.That(game.Ended, Is.True);
        Assert.That((game.Get(3)!.Rank, game.Get(1)!.Rank, game.Get(2)!.Rank, game.Winner), Is.EqualTo((2, 1, 1, 1L)));
        Assert.That(new SurvivalGameState([1, 2], id => units[id]).Ended, Is.True);

        var queue = new ArenaQueue();
        queue.Register(9, MapleSurvivalRules.Arena, 3, 0, 3, out _);
        queue.RegisterSquad([5, 6, 8], MapleSurvivalRules.Arena, 3, 1, 3, out List<ArenaMatch> made);
        Assert.That(made.Single().Characters, Is.EquivalentTo(new long[] { 9, 5, 6, 8 }));
        Assert.That(made.Single().Units[6], Is.EqualTo(5));
    }

    // 0x37 of the party: the squad registered and the line (the client's register / unregister message).
    [Test]
    public void SquadPartyLayout() {
        Assert.That(Hex(PartyPacket.SurvivalPartySearch(true)), Is.EqualTo(Hex(PartyPacket.SurvivalPartySearch(true, true))));
        Assert.That(Hex(PartyPacket.SurvivalPartySearch(false, false))[^6..], Is.EqualTo("37" + "00" + "00"));
        Assert.That(Hex(PartyPacket.SurvivalPartySearch(true, true))[^6..], Is.EqualTo("37" + "01" + "01"));
    }

    [Test]
    public void Game_OnePlayerEndsAtOnce() {
        var game = new SurvivalGameState([9]);
        Assert.That((game.Ended, game.Winner), Is.EqualTo((true, 9L)));
    }

    // The storm: 8 equal steps from the start radius to the smallest; a step's time over after StepMs.
    [Test]
    public void Storm_Steps() {
        var storm = new SurvivalStorm(8_300, 300);
        Assert.That(storm.Radius(0), Is.EqualTo(8_300));
        Assert.That(storm.Radius(4), Is.EqualTo(4_300));
        Assert.That(storm.Radius(8), Is.EqualTo(300));
        Assert.That(storm.TimeOver(0), Is.False);
        storm.Enter(1, 1_000);
        Assert.That(storm.SafeRadius, Is.EqualTo(7_300));
        Assert.That(storm.TimeOver(1_000 + SurvivalStorm.StepMs - 1), Is.False);
        Assert.That(storm.TimeOver(1_000 + SurvivalStorm.StepMs), Is.True);
        Assert.That(SurvivalStorm.Outside(3, 4, 5), Is.False);
        Assert.That(SurvivalStorm.Outside(3, 4.1f, 5), Is.True);
    }

    // A storm step through the widget: the zone once (the circle before the step, the step's circle as the preview),
    // then ready (4), the countdown (6), start (5) and the new radius (1) as a float; the next step no zone again.
    [Test]
    public void Storm_StepPackets() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        var widget = new Maple2.Server.Game.Model.Widget.SurvivalContentsWidget(session.Field!);
        test.TakeSent();
        widget.Action("EnterStep", 0, "1");
        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        string zero = Float(0) + Float(0) + Float(0);
        // No StormData: the fallback storm of 3000 to 300 (step 1: 2662.5).
        Assert.That(sent, Is.EqualTo(new[] {
            "1601" + "00" + "0100" + "B80B" + "0100" + zero + "660A" + zero,
            "1601" + "04" + "0A00",
            "1601" + "06" + "3C00",
            "1601" + "05",
            "1601" + "01" + "0100" + Float(2662.5f) + zero,
        }));
        widget.Action("EnterStep", 0, "2");
        sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        Assert.That(sent.Select(packet => packet[..6]), Is.EqualTo(new[] { "160104", "160106", "160105", "160101" }));
        Assert.That(sent[^1], Is.EqualTo("1601" + "01" + "0100" + Float(2325f) + zero));
    }

    // The windows' packets (layouts.md 2): the lobby value, the season, the skin equipped, the pass progress, the
    // spectate answer (SpectateMushtopia 0x11A mode 1), the record (0x17) and the skin slots in slot order.
    [Test]
    public void WindowLayouts() {
        Assert.That(Hex(MapleSurvivalPacket.LobbyValue(7)), Is.EqualTo("1901" + "12" + "07000000"));
        Assert.That(Hex(MapleSurvivalPacket.Season(1, true, 0x10, "S", 1030, 4, 1, 9)), Is.EqualTo("1901" + "16" + "01000000" + "01000000"
            + Long(0x10) + "0100" + "5300" + "06040000" + "04000000" + "01000000" + "09000000" + "00000000" + "00000000" + "00000000"));
        Assert.That(Hex(MapleSurvivalPacket.SkinEquipped(2, 0x1234)), Is.EqualTo("1901" + "20" + "02" + "34120000"));
        Assert.That(Hex(MapleSurvivalPacket.PassProgress(3, 0)), Is.EqualTo("1901" + "23" + "01000000" + "03000000" + "01000000" + "00000000"));
        Assert.That(Hex(MapleSurvivalPacket.Spectate(0x55)), Is.EqualTo("1A01" + "01" + Long(0x55)));
        var account = new Maple2.Model.Game.Account { Username = "a", Id = 9, SurvivalLevel = 4, SurvivalExp = 120, ActiveGoldPass = true,
            SurvivalSilverLevelRewardClaimed = 3, SurvivalGoldLevelRewardClaimed = 2 };
        Assert.That(Hex(SurvivalPacket.UpdateStats(account, 50)), Is.EqualTo("1901" + "17" + Long(9) + "00000000" + "01" + Long(120) + "04000000"
            + "03000000" + "02000000" + Long(50)));
        var inventory = new Dictionary<MedalType, Dictionary<int, Medal>> {
            [MedalType.Gliding] = new() { [30] = new Medal(30, MedalType.Gliding) { ExpiryTime = 0 } },
            [MedalType.Tail] = new(),
            [MedalType.Riding] = new(),
        };
        var equips = new Dictionary<MedalType, Medal> {
            [MedalType.Tail] = new Medal(0, MedalType.Tail), [MedalType.Riding] = new Medal(0, MedalType.Riding), [MedalType.Gliding] = new Medal(30, MedalType.Gliding),
        };
        Assert.That(Hex(SurvivalPacket.LoadMedals(inventory, equips)), Is.EqualTo("1901" + "1E" + "03"
            + "00000000" + "00000000" + "00000000" + "00000000" + "1E000000" + "01000000" + "1E000000" + Long(long.MaxValue)));
    }

    private static MapleSurvivalTable Table() {
        var reward = new MapleSurvivalTable.Reward("item", [20000995], [1], [10]);
        return new MapleSurvivalTable(
            new Dictionary<int, MapleSurvivalTable.Period> { [1] = new(1, 1, new DateTime(2018, 8, 22), new DateTime(2099, 1, 1), 800) },
            new Dictionary<int, long> { [1] = 0, [2] = 2400, [3] = 4320 },
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> { [1] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>() },
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> { [2] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> { [1] = [reward] } });
    }

    // Exp: level L goes up once the exp reaches reqExp of L + 1 (the rest stays); the last level keeps 0. The pass
    // claim: free up to the level, paid only with the pass. The rating, the game exp and the result ints.
    [Test]
    public void Rules() {
        MapleSurvivalTable table = Table();
        int level = 1;
        long exp = 0;
        Assert.That(SurvivalRules.AddExp(table, ref level, ref exp, 2500), Is.EqualTo(new[] { 2 }));
        Assert.That((level, exp), Is.EqualTo((2, 100L)));
        Assert.That(SurvivalRules.AddExp(table, ref level, ref exp, 9000), Is.EqualTo(new[] { 3 }));
        Assert.That((level, exp), Is.EqualTo((3, 0L)));
        Assert.That(SurvivalRules.Claim(5, 2, 0, false, 8, 8), Is.EqualTo((5, 0)));
        Assert.That(SurvivalRules.Claim(5, 2, 1, true, 1, 8), Is.EqualTo((3, 5)));
        Assert.That(SurvivalRules.Claim(2, 4, 0, true, 1, 0), Is.EqualTo((4, 0)));
        Assert.That(SurvivalRules.RatingChange(1, 20), Is.EqualTo(30));
        Assert.That(SurvivalRules.RatingChange(10, 20), Is.EqualTo(10));
        Assert.That(SurvivalRules.RatingChange(11, 20), Is.EqualTo(-10));
        Assert.That(SurvivalRules.GameExp(2, 20, 3), Is.EqualTo(1000 + 900 + 3600));
        Assert.That(SurvivalRules.CurrentPeriod(table, new DateTime(2026, 11, 12))?.Id, Is.EqualTo(1));
        Assert.That(SurvivalRules.CurrentPeriod(table, new DateTime(2017, 1, 1)), Is.Null);
        // rank 2 of 20, 3 kills, in season: r0 1, r4 participants, r5 rank, r12 / r13 the levels, r15 the kills.
        int[] values = SurvivalRules.ResultValues(2, 20, 3, true, 1000, 1010, 1, 0, 5500, 2, 3100, 125, 61, 7);
        Assert.That(values, Is.EqualTo(new[] { 1, 0, 1000, 1010, 20, 2, 1, 0, 5500, 3100, 2, 0, 61, 7, 125, 3 }));
    }

    // 0xC2 0x22 buys the paid pass at the season's price in merets (any other price or a second buy changes nothing);
    // 0xC2 0x23 claims: the progress (0x23) then the record (0x17).
    [Test]
    public void Pass_BuyAndClaim() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.SetTable(Table());
        session.Currency.Meret = 1000;
        session.Player.Value.Account.SurvivalLevel = 3;
        test.TakeSent();
        List<string> sent = Send(test, session, "22" + "01" + Long(799));
        Assert.That(sent.Single(), Does.StartWith("1901" + "17"));
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.False);
        sent = Send(test, session, "22" + "01" + Long(800));
        Assert.That((session.Player.Value.Account.ActiveGoldPass, session.Currency.Meret), Is.EqualTo((true, 200L)));
        Assert.That(sent.Where(packet => packet.StartsWith("1901")).Select(packet => packet[..6]), Is.EqualTo(new[] { "190123", "190117" }));
        sent = Send(test, session, "23" + "08000000" + "00000000");
        Assert.That(sent.Where(packet => packet.StartsWith("1901")).ToList(), Is.EqualTo(new[] {
            "1901" + "23" + "01000000" + "03000000" + "01000000" + "00000000",
            Hex(SurvivalPacket.UpdateStats(session.Player.Value.Account)) }));
        Assert.That(session.Player.Value.Account.SurvivalSilverLevelRewardClaimed, Is.EqualTo(3));
    }

    // The script's 20 players is the match's own count in a survival match's field only.
    [Test]
    public void ScriptCount() {
        Assert.That(MapleSurvivalRules.ScriptCount(20, false), Is.EqualTo(20));
        Assert.That(MapleSurvivalRules.ScriptCount(2, true), Is.EqualTo(2));
        Assert.That(MapleSurvivalRules.ScriptCount(20, true), Is.EqualTo(MapleSurvivalRules.PlayerCount));
    }

    private static List<string> Send(TestGameSession test, GameSession session, string hex) {
        test.TakeSent();
        new MapleSurvivalHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteBytes(Convert.FromHexString(hex))));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    // 0xC2: queue (state 1, then the World's arena 100 at the full count), cancel (state 0), refuse (state 0, a decline),
    // accept (the move into the match field).
    [Test]
    public void Requests() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Assert.That(Send(test, session, "00"), Is.EqualTo(new[] { "1901" + "00" }));
        PvpRequest register = test.World.PvpRequests.Single();
        Assert.That((register.ArenaRegister.Arena, register.ArenaRegister.PlayerCount, register.ArenaRegister.MinPlayers),
            Is.EqualTo((MapleSurvivalRules.Arena, MapleSurvivalRules.PlayerCount, MapleSurvivalRules.PlayerCount)));
        Assert.That(Send(test, session, "02"), Is.EqualTo(new[] { "1901" + "01" }));
        Assert.That(Send(test, session, "03" + "00"), Is.EqualTo(new[] { "1901" + "01" }));
        Assert.That(test.World.PvpRequests.Select(request => request.PvpCase), Is.EqualTo(new[] {
            PvpRequest.PvpOneofCase.ArenaRegister, PvpRequest.PvpOneofCase.ArenaUnregister, PvpRequest.PvpOneofCase.ArenaDecline }));
        test.World.PvpResponses[PvpRequest.PvpOneofCase.ArenaJoin] = new PvpResponse { Channel = 1, MapId = MapleSurvivalRules.MapId, RoomId = 41 };
        Send(test, session, "01");
        MigrateOutRequest move = test.World.Moves.Single();
        Assert.That((move.MapId, move.RoomId, move.Type), Is.EqualTo((MapleSurvivalRules.MapId, 41, MigrationType.PvpMatch)));
    }
}
