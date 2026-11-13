using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Web.Controllers;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;
using ChannelGuildRequest = Maple2.Server.Channel.Service.GuildRequest;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;
using InGameRankPacket = Maple2.Server.Web.Packet.InGameRankPacket;
using PvpRound = Maple2.Server.World.Service.GuildRequest.Types.PvpEnd.Types.Round;

namespace Maple2.Server.Tests.Game;

// The guild championship (guild PvP): its game event, the registration, the match and its result.
public class GuildChampionshipTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const long GuildId = 7001;

    // Before, the table's GuildChampionship row (KR 99000037) was dropped at ingest and never sent, so
    // the client's championship flag (CGameRule::SetGuildChampionshipOpen) stayed off.
    [Test]
    public void GameEvent_GuildChampionshipIsTheIdOnly() {
        var metadata = new GameEventMetadata(99000037, GameEventType.GuildChampionship, new DateTime(2023, 3, 19),
            new DateTime(2100, 1, 1), TimeSpan.Zero, new TimeSpan(23, 59, 59), [], new GuildChampionship(), "", "", "", "");
        var gameEvent = new GameEvent(metadata);
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("GuildChampionship")); // GameEventGuildChampionship
        Assert.That(reader.ReadInt(), Is.EqualTo(99000037)); // GameEvent::vfunc_4
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    #region Packets
    private static ByteReader Guild(ByteWriter writer, byte command) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        return reader;
    }

    private static void End(ByteReader reader, ByteWriter writer) => Assert.That(reader.Position, Is.EqualTo(writer.Length));

    // 0x27 (CGuildInfo::SetPvpState): the state byte.
    [Test]
    public void PvpState_IsTheStateByte() {
        ByteWriter writer = GuildPacket.PvpState(GuildPvpState.Matched);
        ByteReader reader = Guild(writer, 0x27);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) GuildPvpState.Matched));
        End(reader, writer);
    }

    // 0x28: matched or not.
    [Test]
    public void PvpMatched_IsABool() {
        ByteWriter writer = GuildPacket.PvpMatched(true);
        ByteReader reader = Guild(writer, 0x28);
        Assert.That(reader.ReadBool(), Is.True);
        End(reader, writer);
    }

    // 0x29: score, wins, losses; 0x2a: won and the opponent's name.
    [Test]
    public void PvpRatingAndResult() {
        ByteWriter writer = GuildPacket.PvpRating(1030, 4, 2);
        ByteReader reader = Guild(writer, 0x29);
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((1030, 4, 2)));
        End(reader, writer);

        writer = GuildPacket.PvpResult(false, "Red");
        reader = Guild(writer, 0x2a);
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Red"));
        End(reader, writer);
    }

    private static Guild CountGuild(int seasonId) {
        var guild = new Guild(9001, "Blue", 1, 11, "Ann") { Pvp = new GuildPvp { SeasonId = seasonId } };
        foreach ((long id, string name) in new[] { (11L, "Ann"), (12L, "Bo"), (13L, "Cy") }) {
            var info = new PlayerInfo(new CharacterInfo(1, id, name, "", "", Gender.Male, Job.Newbie, 50), "", new AchievementInfo(), []);
            guild.Members[id] = new GuildMember { GuildId = guild.Id, Info = info };
        }
        return guild;
    }

    // 0x26 (CGuildManager::FUN_1413c23d0): the count, then per member the name and the season and total counts.
    [Test]
    public void PvpMemberCounts_AreNameSeasonTotal() {
        Guild guild = CountGuild(1);
        guild.Members[11].PvpSeasonCount = 3;
        guild.Members[11].PvpTotalCount = 9;
        guild.Members[13].PvpSeasonCount = 1;
        guild.Members[13].PvpTotalCount = 2;
        ByteWriter writer = GuildPacket.PvpMemberCounts([guild.Members[11], guild.Members[13]]);
        ByteReader reader = Guild(writer, 0x26);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That((reader.ReadUnicodeString(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo(("Ann", 3, 9)));
        Assert.That((reader.ReadUnicodeString(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo(("Cy", 1, 2)));
        End(reader, writer);
    }

    // A match's counts: only the fighters whose counts moved are listed and changed.
    [Test]
    public void ApplyMemberCounts_SameSeason_SetsTheFighters() {
        Guild guild = CountGuild(1);
        guild.Members[12].PvpSeasonCount = 4;
        guild.Members[12].PvpTotalCount = 4;
        guild.Members[13].PvpSeasonCount = 2;
        guild.Members[13].PvpTotalCount = 5;

        List<GuildMember> changed = GuildPvp.ApplyMemberCounts(guild, 1, [(11, 1, 1), (13, 2, 5), (99, 7, 7)]);

        Assert.That(changed.Select(member => member.CharacterId), Is.EqualTo(new[] { 11L }));
        Assert.That((guild.Members[11].PvpSeasonCount, guild.Members[11].PvpTotalCount), Is.EqualTo((1, 1)));
        Assert.That((guild.Members[12].PvpSeasonCount, guild.Members[12].PvpTotalCount), Is.EqualTo((4, 4)));
        Assert.That((guild.Members[13].PvpSeasonCount, guild.Members[13].PvpTotalCount), Is.EqualTo((2, 5)));
    }

    // The World rolled the record into a new season (its members' season counts start again): the copy's members
    // who did not fight go to 0 as well, and are listed with the fighters.
    [Test]
    public void ApplyMemberCounts_NewSeason_StartsTheSeasonCountsAgain() {
        Guild guild = CountGuild(1);
        guild.Members[11].PvpSeasonCount = 6;
        guild.Members[11].PvpTotalCount = 6;
        guild.Members[12].PvpSeasonCount = 3;
        guild.Members[12].PvpTotalCount = 8;

        List<GuildMember> changed = GuildPvp.ApplyMemberCounts(guild, 2, [(11, 1, 7)]);

        Assert.That(changed.Select(member => member.CharacterId), Is.EquivalentTo(new[] { 11L, 12L }));
        Assert.That((guild.Members[11].PvpSeasonCount, guild.Members[11].PvpTotalCount), Is.EqualTo((1, 7)));
        Assert.That((guild.Members[12].PvpSeasonCount, guild.Members[12].PvpTotalCount), Is.EqualTo((0, 8)));
        Assert.That((guild.Members[13].PvpSeasonCount, guild.Members[13].PvpTotalCount), Is.EqualTo((0, 0)));
    }

    // 0x2b, 0x2c: the member's name.
    [Test]
    public void PvpRegisteredAndUnregistered_AreTheName() {
        foreach ((ByteWriter writer, byte command) in new[] { (GuildPacket.PvpRegistered("Ann"), (byte) 0x2b), (GuildPacket.PvpUnregistered("Ann"), (byte) 0x2c) }) {
            ByteReader reader = Guild(writer, command);
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
            End(reader, writer);
        }
    }

    // 0x4a (CGuildPVPHistoryData::ReadStream): per row a byte 1 and the row; five rounds of fighters and results.
    [Test]
    public void PvpHistory_WritesTheRow() {
        var match = new GuildPvpMatch {
            GuildA = 1,
            GuildB = 2,
            NameA = "Blue",
            NameB = "Red",
            Time = 1700000000,
            PlayersA = ["11,a", "", "", "", ""],
            PlayersB = ["21,b", "", "", "", ""],
            Results = [(byte) 'W', 0, 0, 0, 0],
        };
        ByteWriter writer = GuildPacket.PvpHistory([match]);
        ByteReader reader = Guild(writer, 0x4a);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That((reader.ReadInt(), reader.ReadInt()), Is.EqualTo((0, 0)));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((1L, 2L)));
        Assert.That((reader.ReadUnicodeString(), reader.ReadUnicodeString()), Is.EqualTo(("Blue", "Red")));
        for (int i = 0; i < 4; i++) {
            reader.ReadInt();
        }
        Assert.That((reader.ReadUnicodeString(), reader.ReadUnicodeString(), reader.ReadByte()), Is.EqualTo(("11,a", "21,b", (byte) 'W')));
        for (int i = 1; i < 5; i++) {
            Assert.That((reader.ReadUnicodeString(), reader.ReadUnicodeString(), reader.ReadByte()), Is.EqualTo(("", "", (byte) 0)));
        }
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((1700000000L, 1700000000L)));
        End(reader, writer);
    }

    // 0x4b (CGuildPVPSeasonHistoryData): id, start, end, name, score, rank, grade, a string, an int, wins, losses, 4 ints.
    [Test]
    public void PvpSeason_WritesTheRow() {
        var season = new GuildPvpSeason { SeasonId = 3, StartTime = 10, EndTime = 20, Name = "March 2026", Score = 1200, Rank = 2, Grade = 1, Wins = 5, Losses = 1 };
        ByteWriter writer = GuildPacket.PvpSeason([season]);
        ByteReader reader = Guild(writer, 0x4b);
        Assert.That((reader.ReadInt(), reader.ReadByte()), Is.EqualTo((1, (byte) 1)));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((10L, 20L)));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("March 2026"));
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((1200, 2, 1)));
        Assert.That(reader.ReadString(), Is.Empty);
        Assert.That(reader.ReadInt(), Is.Zero);
        Assert.That((reader.ReadInt(), reader.ReadInt()), Is.EqualTo((5, 1)));
        for (int i = 0; i < 4; i++) {
            Assert.That(reader.ReadInt(), Is.Zero);
        }
        End(reader, writer);
    }

    private static ByteReader Pvp(ByteWriter writer, byte command) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Pvp));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        return reader;
    }

    // Pvp 4 (SetTargetGuildAndNotify): the guild and its name.
    [Test]
    public void ChampionGuild_IsGuildAndName() {
        ByteWriter writer = PvpPacket.ChampionGuild(GuildId, "Blue");
        ByteReader reader = Pvp(writer, 4);
        Assert.That(reader.ReadLong(), Is.EqualTo(GuildId));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Blue"));
        End(reader, writer);
    }

    // Pvp 'r': each guild's id, name and emblem.
    [Test]
    public void GuildTeams_AreBothGuilds() {
        ByteWriter writer = PvpPacket.GuildTeams(1, "Blue", "b.png", 2, "Red", "r.png");
        ByteReader reader = Pvp(writer, (byte) 'r');
        Assert.That((reader.ReadLong(), reader.ReadUnicodeString(), reader.ReadUnicodeString()), Is.EqualTo((1L, "Blue", "b.png")));
        Assert.That((reader.ReadLong(), reader.ReadUnicodeString(), reader.ReadUnicodeString()), Is.EqualTo((2L, "Red", "r.png")));
        End(reader, writer);
    }

    // Pvp 'u': the winner's and the loser's block, then the reward count.
    [Test]
    public void GuildResult_WinnerFirstThenRewards() {
        ByteWriter writer = PvpPacket.GuildResult(new PvpPacket.GuildSide("Blue", 1, 3, 1030, 30, "b.png"),
            new PvpPacket.GuildSide("Red", 2, 1, 980, -20, "r.png"), 100);
        ByteReader reader = Pvp(writer, (byte) 'u');
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Blue"));
        Assert.That((reader.ReadLong(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((1L, 3, 1030, 30)));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b.png"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Red"));
        Assert.That((reader.ReadLong(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((2L, 1, 980, -20)));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("r.png"));
        Assert.That(reader.ReadInt(), Is.EqualTo(100));
        End(reader, writer);
    }

    // TeamPvp 's': round, end tick, 0, the fighters, then each guild's round wins; 't': the winner guild.
    [Test]
    public void TeamPvp_RoundAndEnd() {
        ByteWriter writer = TeamPvpPacket.Round(2, 5000, [11L, 21L], 1, 1, 2, 0);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.TeamPvp));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) 's'));
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((2, 5000, 0, 2)));
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((11L, 21L)));
        Assert.That((reader.ReadLong(), reader.ReadInt(), reader.ReadLong(), reader.ReadInt()), Is.EqualTo((1L, 1, 2L, 0)));
        End(reader, writer);

        writer = TeamPvpPacket.End(2);
        reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.TeamPvp));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) 't'));
        Assert.That(reader.ReadLong(), Is.EqualTo(2));
        End(reader, writer);
    }

    // The ranking board 0x35 (GameRankingItem::GuildPvp::Read): rank, id, name, emblem, leader, score, grade.
    [Test]
    public void RankingBoard_RowsByStandingWithGrade() {
        var season = new SeasonDataTable.Entry(1, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1), [1100, 1300]);
        IList<GuildPvpRankRow> rows = GuildPvpRanking.Rows([
            new GuildPvpStanding(1, "Blue", "b.png", "Ann", 1150, 0),
            new GuildPvpStanding(2, "Red", "r.png", "Bob", 990, 2),
        ], season);
        Assert.That(rows.Select(row => (row.Rank, row.Grade)), Is.EqualTo(new[] { (1, 1), (2, 0) }));

        ByteWriter writer = InGameRankPacket.GuildPvp(GameRankingType.GuildPvp, rows);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That((reader.ReadInt(), reader.ReadInt()), Is.EqualTo((53, 0)));
        reader.ReadUnicodeStringWithLength();
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That((reader.ReadInt(), reader.ReadLong()), Is.EqualTo((1, 1L)));
        Assert.That((reader.ReadUnicodeStringWithLength(), reader.ReadUnicodeStringWithLength(), reader.ReadUnicodeStringWithLength()),
            Is.EqualTo(("Blue", "b.png", "Ann")));
        Assert.That((reader.ReadInt(), reader.ReadInt()), Is.EqualTo((1150, 1)));
        Assert.That((reader.ReadInt(), reader.ReadLong()), Is.EqualTo((2, 2L)));
        Assert.That((reader.ReadUnicodeStringWithLength(), reader.ReadUnicodeStringWithLength(), reader.ReadUnicodeStringWithLength()),
            Is.EqualTo(("Red", "r.png", "Bob")));
        Assert.That((reader.ReadInt(), reader.ReadInt()), Is.EqualTo((990, 0)));
        End(reader, writer);
    }

    // The board's season: the current one for 0x35 and 0x33, the asked one for 0x36 and 0x34; none outside a season.
    [Test]
    public void RankingBoard_Season() {
        var seasons = new Dictionary<int, SeasonDataTable.Entry> {
            [4] = new(4, new DateTime(2026, 11, 1), new DateTime(2026, 12, 1), []),
        };
        var now = new DateTime(2026, 11, 13);
        Assert.That(GuildPvpRanking.Season(GameRankingType.GuildPvp, 0, seasons, now), Is.EqualTo(4));
        Assert.That(GuildPvpRanking.Season(GameRankingType.GuildPvpPersonal, 0, seasons, now), Is.EqualTo(4));
        Assert.That(GuildPvpRanking.Season(GameRankingType.GuildPvpPreviousSeason, 3, seasons, now), Is.EqualTo(3));
        Assert.That(GuildPvpRanking.Season(GameRankingType.GuildPvp, 0, seasons, new DateTime(2027, 1, 1)), Is.Zero);
    }
    #endregion

    #region Zone rules
    private static readonly int[] Boxes = [102, 103, 112, 113, 10, 11, 1, 3];

    private static GuildPvpZoneState Zone() => new(101, 0, 0, 60, Boxes);

    private static readonly Dictionary<int, IReadOnlyList<long>> Stands = new() {
        [GuildPvpZoneState.TeamA] = [11, 12, 13, 14],
        [GuildPvpZoneState.TeamB] = [21, 22],
    };

    private static List<GuildPvpZoneEvent> Start(GuildPvpZoneState zone, long tick, Func<long, bool>? inFight = null) {
        return zone.Update(tick, team => Stands[team], _ => [], inFight ?? (_ => true));
    }

    [Test]
    public void Zone_BoxesFromTheScript() {
        GuildPvpZoneState zone = Zone();
        Assert.That((zone.StandA, zone.StandB, zone.AreaA, zone.AreaB, zone.PortalA, zone.PortalB, zone.ArenaPortal, zone.WinsNeeded),
            Is.EqualTo((102, 103, 112, 113, 10, 11, 1, 3)));
        Assert.That(new GuildPvpZoneState(101, 0, 0, 60, [102, 103]).WinsNeeded, Is.EqualTo(GuildPvpZoneState.DefaultWins));
    }

    // A round takes up to 3 fighters a side; fighters of opposite teams are hostile, of one team not.
    [Test]
    public void Zone_RoundStartsWithThreeFightersASide() {
        GuildPvpZoneState zone = Zone();
        List<GuildPvpZoneEvent> events = Start(zone, 0);
        Assert.That(events.Single().Type, Is.EqualTo(GuildPvpZoneEventType.RoundStart));
        Assert.That(zone.Fighters(GuildPvpZoneState.TeamA), Is.EqualTo(new long[] { 11, 12, 13 }));
        Assert.That(zone.Fighters(GuildPvpZoneState.TeamB), Is.EqualTo(new long[] { 21, 22 }));
        Assert.That(zone.Hostile(11, 21), Is.True);
        Assert.That(zone.Hostile(11, 12), Is.False);
        Assert.That(zone.Hostile(11, 14), Is.False);
        Assert.That(zone.RoundEndTick, Is.EqualTo(60_000));
    }

    // The team with nobody left loses the round at once; the next round starts after the break.
    [Test]
    public void Zone_EliminationEndsTheRound() {
        GuildPvpZoneState zone = Zone();
        Start(zone, 0);
        Assert.That(zone.Out(21, 1000).Single().Type, Is.EqualTo(GuildPvpZoneEventType.Out));
        List<GuildPvpZoneEvent> events = zone.Out(22, 2000);
        Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { GuildPvpZoneEventType.Out, GuildPvpZoneEventType.RoundEnd }));
        Assert.That(events[1].Team, Is.EqualTo(GuildPvpZoneState.TeamA));
        Assert.That(zone.Wins(GuildPvpZoneState.TeamA), Is.EqualTo(1));
        Assert.That(Start(zone, 2000 + GuildPvpZoneState.BreakMs - 1), Is.Empty);
        Assert.That(Start(zone, 2000 + GuildPvpZoneState.BreakMs).Single().Type, Is.EqualTo(GuildPvpZoneEventType.RoundStart));
        Assert.That(zone.Round, Is.EqualTo(2));
    }

    // Leaving the fight box is out; at the round's time the team with more fighters left wins, equal is a draw.
    [Test]
    public void Zone_RingOutAndTimeout() {
        GuildPvpZoneState zone = Zone();
        Start(zone, 0);
        List<GuildPvpZoneEvent> events = Start(zone, 1000, id => id != 11);
        Assert.That(events.Single(), Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.Out, GuildPvpZoneState.TeamA, 11)));
        events = Start(zone, 60_000);
        Assert.That(events.Single(), Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.RoundEnd, GuildPvpZoneState.NoTeam)));
        Assert.That(zone.Rounds.Single().Winner, Is.EqualTo(GuildPvpZoneState.NoTeam));
    }

    // A team with nobody to send loses the round without a fight; the wins needed end the match.
    [Test]
    public void Zone_EmptyTeamLosesAndWinsEndTheMatch() {
        GuildPvpZoneState zone = Zone();
        var events = new List<GuildPvpZoneEvent>();
        for (int i = 0; i < 3; i++) {
            events.AddRange(zone.Update(i * GuildPvpZoneState.BreakMs, team => team == GuildPvpZoneState.TeamA ? [11] : [], _ => [], _ => true));
        }
        Assert.That(events.Count(e => e.Type == GuildPvpZoneEventType.RoundStart), Is.Zero);
        Assert.That(events.Last(), Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.End, GuildPvpZoneState.TeamA)));
        Assert.That((zone.Ended, zone.Winner, zone.Rounds.Count), Is.EqualTo((true, GuildPvpZoneState.TeamA, 3)));
        Assert.That(zone.Update(1_000_000, _ => [11], _ => [], _ => true), Is.Empty);
    }

    // A walkover is not a fought round: no fighters, so no points, reward or participation count for anyone.
    [Test]
    public void Zone_WalkoverIsNotFought() {
        GuildPvpZoneState zone = Zone();
        for (int i = 0; i < 3; i++) {
            zone.Update(i * GuildPvpZoneState.BreakMs, team => team == GuildPvpZoneState.TeamA ? [11, 12] : [], _ => [], _ => true);
        }
        Assert.That(zone.Ended, Is.True);
        Assert.That(zone.Rounds.Select(round => round.Fought), Is.All.False);
        Assert.That(zone.Rounds.SelectMany(round => round.FightersA.Concat(round.FightersB)), Is.Empty);
        Assert.That((zone.FoughtRounds, zone.RoundFighters.Count()), Is.EqualTo((0, 0)));
    }

    // The match's fighters are those of the fought rounds, each once; a player who never fought is not one.
    [Test]
    public void Zone_RoundFightersAreTheFoughtRoundsOnly() {
        GuildPvpZoneState zone = Zone();
        Start(zone, 0);
        zone.Out(21, 1);
        zone.Out(22, 2);
        // Round 2: guild B has nobody to send (a walkover won by A, its stand fighters not counted again).
        zone.Update(2 + GuildPvpZoneState.BreakMs, team => team == GuildPvpZoneState.TeamA ? [14] : [], _ => [], _ => true);
        Assert.That(zone.Rounds.Select(round => round.Fought), Is.EqualTo(new[] { true, false }));
        Assert.That(zone.FoughtRounds, Is.EqualTo(1));
        Assert.That(zone.RoundFighters, Is.EquivalentTo(new long[] { 11, 12, 13, 21, 22 }));
    }

    // A guild seen in the field with no player left in it after the start forfeits: the guild that stayed wins.
    [Test]
    public void Zone_GuildGoneForfeits() {
        GuildPvpZoneState zone = Zone();
        Assert.That(zone.Update(0, team => Stands[team], _ => [], _ => true, _ => true).Single().Type,
            Is.EqualTo(GuildPvpZoneEventType.RoundStart));
        zone.Out(11, 1);
        zone.Out(12, 2);
        zone.Out(13, 3);
        List<GuildPvpZoneEvent> events = zone.Update(4, team => Stands[team], _ => [], _ => true, team => team == GuildPvpZoneState.TeamA);
        Assert.That(events.Single(), Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.End, GuildPvpZoneState.TeamA)));
        Assert.That((zone.Ended, zone.MatchWinner, zone.Winner), Is.EqualTo((true, GuildPvpZoneState.TeamA, GuildPvpZoneState.TeamB)));

        // Before, a field nobody had entered yet ended at the start as a draw; nobody seen forfeits nothing, and its
        // rounds wait for the guilds until the walkover grace ends (then a draw round).
        GuildPvpZoneState empty = Zone();
        Assert.That(empty.Update(0, _ => [], _ => [], _ => true, _ => false), Is.Empty);
        Assert.That(empty.Update(GuildPvpZoneState.WalkoverGraceMs, _ => [], _ => [], _ => true, _ => false).Single(),
            Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.RoundEnd, GuildPvpZoneState.NoTeam)));
        Assert.That(empty.Ended, Is.False);
    }

    // Before, a guild not in the field at the start forfeited at once: the first guild in won the match. Now a guild
    // never seen forfeits nothing, and once it arrives the rounds are fought.
    [Test]
    public void Zone_LateArrivalDoesNotForfeit() {
        GuildPvpZoneState zone = Zone();
        bool arrived = false;
        Func<int, IReadOnlyList<long>> stands = team => team == GuildPvpZoneState.TeamA || arrived ? Stands[team] : [];
        Func<int, bool> present = team => team == GuildPvpZoneState.TeamA || arrived;
        Assert.That(zone.Update(0, stands, _ => [], _ => true, present), Is.Empty);
        Assert.That(zone.Forfeit(GuildPvpZoneState.TeamB, 1), Is.Empty, "a guild never seen does not forfeit");

        arrived = true;
        List<GuildPvpZoneEvent> events = zone.Update(GuildPvpZoneState.BreakMs, stands, _ => [], _ => true, present);
        Assert.That(events.Single().Type, Is.EqualTo(GuildPvpZoneEventType.RoundStart));
        Assert.That((zone.Round, zone.Rounds.Count), Is.EqualTo((1, 0)));
    }

    // rv320 1. Before, the walkovers at the start, +10 s and +20 s ended the match before a guild that accepted the
    // join box late (and loads slowly) arrived. No walkover before both guilds were seen or the grace ended.
    [Test]
    public void Zone_LateGuildGetsTheGrace() {
        GuildPvpZoneState zone = Zone();
        bool arrived = false;
        Func<int, IReadOnlyList<long>> stands = team => team == GuildPvpZoneState.TeamA || arrived ? Stands[team] : [];
        Func<int, bool> present = team => team == GuildPvpZoneState.TeamA || arrived;
        for (int i = 0; i < 3; i++) {
            Assert.That(zone.Update(i * GuildPvpZoneState.BreakMs, stands, _ => [], _ => true, present), Is.Empty);
        }
        Assert.That((zone.Ended, zone.Rounds.Count), Is.EqualTo((false, 0)));
        arrived = true;
        Assert.That(zone.Update(25_000, stands, _ => [], _ => true, present).Single().Type, Is.EqualTo(GuildPvpZoneEventType.RoundStart));

        // A guild that never comes loses by walkover once the grace is over.
        GuildPvpZoneState alone = Zone();
        long grace = GuildPvpZoneState.WalkoverGraceMs;
        Func<int, IReadOnlyList<long>> standA = team => team == GuildPvpZoneState.TeamA ? Stands[team] : [];
        Assert.That(alone.Update(grace - 1, standA, _ => [], _ => true, team => team == GuildPvpZoneState.TeamA), Is.Empty);
        for (int i = 0; i < 3; i++) {
            alone.Update(grace + i * GuildPvpZoneState.BreakMs, standA, _ => [], _ => true, team => team == GuildPvpZoneState.TeamA);
        }
        Assert.That((alone.Ended, alone.MatchWinner, alone.FoughtRounds), Is.EqualTo((true, GuildPvpZoneState.TeamA, 0)));
    }

    // rv320 4. Before, a guild never in the field won by forfeit when the other guild's only player came and left
    // (a win and a loss counted). A forfeit's winner must have been seen, else the match is a draw.
    [Test]
    public void Zone_ForfeitToAGuildNeverSeenIsADraw() {
        GuildPvpZoneState zone = Zone();
        Func<int, IReadOnlyList<long>> stands = team => team == GuildPvpZoneState.TeamA ? Stands[team] : [];
        zone.Update(1, stands, _ => [], _ => true, team => team == GuildPvpZoneState.TeamA);
        Assert.That(zone.Forfeit(GuildPvpZoneState.TeamA, 2).Single(), Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.End, GuildPvpZoneState.NoTeam)));
        Assert.That((zone.Ended, zone.MatchWinner), Is.EqualTo((true, GuildPvpZoneState.NoTeam)));

        // The same through the clock: guild A seen, then gone, guild B never there.
        GuildPvpZoneState ticked = Zone();
        ticked.Update(1, stands, _ => [], _ => true, team => team == GuildPvpZoneState.TeamA);
        Assert.That(ticked.Update(2, _ => [], _ => [], _ => true, _ => false).Single(),
            Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.End, GuildPvpZoneState.NoTeam)));
    }

    // The first guild alone in its match field: walkovers only, no fought round won, so no points, reward or trophy.
    [Test]
    public void Zone_FirstGuildAloneWinsNoFoughtRound() {
        GuildPvpZoneState zone = Zone();
        for (int i = 0; i < 3; i++) {
            zone.Update(GuildPvpZoneState.WalkoverGraceMs + i * GuildPvpZoneState.BreakMs, team => team == GuildPvpZoneState.TeamA ? Stands[team] : [],
                _ => [], _ => true, team => team == GuildPvpZoneState.TeamA);
        }
        Assert.That((zone.Ended, zone.MatchWinner), Is.EqualTo((true, GuildPvpZoneState.TeamA)));
        Assert.That(zone.WonFoughtRound(GuildPvpZoneState.TeamA), Is.False);
        Assert.That(WorldService.FoughtMatch(Rounds(zone), GuildPvpZoneState.TeamA), Is.False);
    }

    // A forfeit during a round: the cut round goes to the team that stayed for the result only (not fought, no
    // fighters), the fought rounds before it stay as they were.
    [Test]
    public void Zone_ForfeitCutsTheRoundForTheResultOnly() {
        GuildPvpZoneState zone = Zone();
        zone.Update(0, team => Stands[team], _ => [], _ => true, _ => true);
        zone.Out(21, 1);
        zone.Out(22, 2);
        zone.Update(2 + GuildPvpZoneState.BreakMs, team => Stands[team], _ => [], _ => true, _ => true);
        Assert.That(zone.Fighting, Is.True);
        List<GuildPvpZoneEvent> events = zone.Forfeit(GuildPvpZoneState.TeamA, 3 + GuildPvpZoneState.BreakMs);
        Assert.That(events, Is.EqualTo(new[] {
            new GuildPvpZoneEvent(GuildPvpZoneEventType.RoundEnd, GuildPvpZoneState.TeamB),
            new GuildPvpZoneEvent(GuildPvpZoneEventType.End, GuildPvpZoneState.TeamB),
        }));
        Assert.That(zone.Rounds.Select(round => (round.Fought, round.Winner)), Is.EqualTo(new[] {
            (true, GuildPvpZoneState.TeamA), (false, GuildPvpZoneState.TeamB),
        }));
        Assert.That(zone.Rounds[1].FightersA.Concat(zone.Rounds[1].FightersB), Is.Empty);
        Assert.That(zone.RoundFighters, Is.EquivalentTo(new long[] { 11, 12, 13, 21, 22 }));
        Assert.That((zone.MatchWinner, zone.WonFoughtRound(GuildPvpZoneState.TeamB), zone.WonFoughtRound(GuildPvpZoneState.TeamA)),
            Is.EqualTo((GuildPvpZoneState.TeamB, false, true)));
    }

    private static List<PvpRound> Rounds(GuildPvpZoneState zone) {
        return zone.Rounds.Select(round => new PvpRound { Winner = round.Winner, Fought = round.Fought }).ToList();
    }

    // Before, a fought draw (or a forfeit's cut round) made a fought match. A match without a fought round won keeps
    // no round results, marks the loser no-fight and moves no score and pays nothing.
    [Test]
    public void PvpEnd_NoFoughtRoundWon_MovesNothing() {
        Assert.That(WorldService.FoughtMatch([new PvpRound { Fought = true, Winner = 0 }, new PvpRound { Fought = false, Winner = 1 }], 1), Is.False);
        Assert.That(WorldService.FoughtMatch([new PvpRound { Fought = true, Winner = 2 }], 2), Is.True);

        PvpRound[] rounds = [new PvpRound { Fought = false, Winner = 1, PlayersA = "", PlayersB = "" }];
        GuildPvpMatch winner = WorldService.PvpHistory(rounds, 1, "a", 2, "b", true, true, false, 100);
        GuildPvpMatch loser = WorldService.PvpHistory(rounds, 2, "b", 1, "a", false, false, false, 100);
        Assert.That((winner.NoFight, loser.NoFight), Is.EqualTo((false, true)));
        Assert.That(winner.Results.Concat(loser.Results), Is.All.EqualTo((byte) 0));

        var pvp = new GuildPvp();
        var now = new DateTime(2026, 11, 13, 12, 0, 0);
        Assert.That(pvp.Settle(false, true, 30, now), Is.EqualTo((0, false)));
        Assert.That((pvp.Score, pvp.RewardCount), Is.EqualTo((GuildPvp.DefaultScore, 0)));
        Assert.That(pvp.Settle(true, false, 30, now), Is.EqualTo((0, false)), "no fighter of the guild: no cap slot used");
        Assert.That(pvp.RewardCount, Is.Zero);
    }

    // rv320 2. Before, the World counted a fought round won by either guild while the field's trophies needed the
    // winner's: A wins fought round 1, then B wins on walkovers. One rule now: the winner won a fought round.
    [Test]
    public void PvpEnd_FoughtIsTheWinnersFoughtRound() {
        GuildPvpZoneState zone = Zone();
        zone.Update(0, team => Stands[team], _ => [], _ => true, _ => true);
        zone.Out(21, 1);
        zone.Out(22, 2);
        for (int i = 1; i <= 3; i++) {
            zone.Update(2 + i * GuildPvpZoneState.BreakMs, team => team == GuildPvpZoneState.TeamB ? Stands[team] : [], _ => [], _ => true, _ => true);
        }
        Assert.That((zone.Ended, zone.MatchWinner), Is.EqualTo((true, GuildPvpZoneState.TeamB)));
        Assert.That(zone.WonFoughtRound(GuildPvpZoneState.TeamB), Is.False);
        Assert.That(WorldService.FoughtMatch(Rounds(zone), GuildPvpZoneState.TeamB), Is.False);
        Assert.That(WorldService.FoughtMatch(Rounds(zone), GuildPvpZoneState.TeamA), Is.True);
    }

    // rv320 3. Before, a guild whose fighters were all at their own daily cap still took a guild slot and moved its
    // score (the window showing the change with no reward). The field sends only fighters with a reward left.
    [Test]
    public void PvpEnd_FightersAtTheirCapAreNotPayable() {
        var character = new Character { Name = "Fighter", Mastery = new Mastery() };
        var now = new DateTime(2026, 11, 13, 12, 0, 0);
        Assert.That(GuildPvp.HasFighterReward(character, now), Is.True);
        Assert.That(character.GuildPvpRewardCount, Is.Zero, "asking takes nothing");
        for (int i = 0; i < GuildPvp.DailyRewardCap; i++) {
            GuildPvp.TakeFighterReward(character, true, true, now);
        }
        Assert.That(GuildPvp.HasFighterReward(character, now), Is.False);
        Assert.That(GuildPvp.HasFighterReward(character, now.AddDays(1)), Is.True);

        var pvp = new GuildPvp();
        long[] payable = new long[] { 11 }.Where(_ => GuildPvp.HasFighterReward(character, now)).ToArray();
        Assert.That(pvp.Settle(true, payable.Length > 0, 30, now), Is.EqualTo((0, false)));
        Assert.That((pvp.Score, pvp.RewardCount), Is.EqualTo((GuildPvp.DefaultScore, 0)));
    }

    // Before, the score moved on every fought match past the daily cap. It moves only on a match that pays.
    [Test]
    public void PvpEnd_ScoreOnlyInsideTheCap() {
        var pvp = new GuildPvp();
        var now = new DateTime(2026, 11, 13, 12, 0, 0);
        for (int i = 0; i < GuildPvp.DailyRewardCap; i++) {
            Assert.That(pvp.Settle(true, true, 30, now), Is.EqualTo((30, true)));
        }
        Assert.That(pvp.Settle(true, true, 30, now), Is.EqualTo((0, false)));
        Assert.That(pvp.Score, Is.EqualTo(GuildPvp.DefaultScore + 30 * GuildPvp.DailyRewardCap));
    }

    // Before, every fighter of a paid guild got the reward, a new guild's matches paying again. The reward goes to
    // fighters of a paid guild only, at most DailyRewardCap a day per character whatever the guild.
    [Test]
    public void Result_PaysFightersOnlyUnderTheCharacterCap() {
        var character = new Character { Name = "Fighter", Mastery = new Mastery() };
        var now = new DateTime(2026, 11, 13, 12, 0, 0);
        Assert.That(GuildPvp.TakeFighterReward(character, false, true, now), Is.False, "not a fighter");
        Assert.That(GuildPvp.TakeFighterReward(character, true, false, now), Is.False, "guild not paid");
        Assert.That(character.GuildPvpRewardCount, Is.Zero);
        for (int i = 0; i < GuildPvp.DailyRewardCap; i++) {
            Assert.That(GuildPvp.TakeFighterReward(character, true, true, now), Is.True);
        }
        // Another guild's paid match the same day (a hop or a new guild): nothing more.
        Assert.That(GuildPvp.TakeFighterReward(character, true, true, now.AddHours(1)), Is.False);
        Assert.That(GuildPvp.TakeFighterReward(character, true, true, now.AddDays(1)), Is.True);
    }

    // A guild's matches pay the reward at most DailyRewardCap times a day; the next day starts again.
    [Test]
    public void Reward_DailyCap() {
        var pvp = new GuildPvp();
        var day = new DateTime(2026, 11, 13, 23, 0, 0);
        for (int i = 0; i < GuildPvp.DailyRewardCap; i++) {
            Assert.That(pvp.TakeReward(day), Is.True);
        }
        Assert.That(pvp.TakeReward(day.AddMinutes(30)), Is.False);
        Assert.That(pvp.Clone().TakeReward(day), Is.False, "the count is kept with the record");
        Assert.That(pvp.TakeReward(day.AddHours(1)), Is.True);
    }

    // Without a stand, the team's other players in the field fight; after MaxRounds draws the match is a draw.
    [Test]
    public void Zone_OthersFightAndDrawsEndAfterMaxRounds() {
        GuildPvpZoneState zone = Zone();
        long tick = 0;
        for (int i = 0; i < GuildPvpZoneState.MaxRounds; i++) {
            zone.Update(tick, _ => [], team => team == GuildPvpZoneState.TeamA ? [31] : [41], _ => true);
            Assert.That(zone.Fighters(GuildPvpZoneState.TeamA), Is.EqualTo(new long[] { 31 }));
            tick += 60_000;
            zone.Update(tick, _ => [], _ => [], _ => true);
            tick += GuildPvpZoneState.BreakMs;
        }
        Assert.That((zone.Ended, zone.Winner, zone.Rounds.Count), Is.EqualTo((true, GuildPvpZoneState.NoTeam, GuildPvpZoneState.MaxRounds)));
    }

    // Stop ends the match with the rounds as they stand.
    [Test]
    public void Zone_StopKeepsTheRounds() {
        GuildPvpZoneState zone = Zone();
        Start(zone, 0);
        zone.Out(21, 1);
        zone.Out(22, 2);
        Assert.That(zone.Stop(3).Single(), Is.EqualTo(new GuildPvpZoneEvent(GuildPvpZoneEventType.End, GuildPvpZoneState.TeamA)));
        Assert.That(zone.Stop(4), Is.Empty);
        Assert.That(zone.LastEndTick, Is.EqualTo(3));
    }
    #endregion

    #region World lookup
    private sealed class LookupTest {
        public long Now;
        public bool Open = true;
        public readonly List<(long Guild, ChannelGuildRequest Notice)> Notices = [];
        public readonly List<(int Match, long A, long B)> Fields = [];
        public MigrationError FieldError = MigrationError.ok;
        public readonly GuildPvpLookup Lookup;

        public LookupTest() {
            Lookup = new GuildPvpLookup((matchId, a, b) => {
                Fields.Add((matchId, a, b));
                return (FieldError, 2, 64000001, 55);
            }, (guild, notice) => {
                lock (Notices) {
                    Notices.Add((guild, notice));
                }
            }, () => Open, () => Now, TimeSpan.FromMinutes(1));
        }

        public void Tick() {
            Lookup.Tick();
            Lookup.LastStep.Wait();
        }

        public List<ChannelGuildRequest> Of(long guild) {
            lock (Notices) {
                return Notices.Where(n => n.Guild == guild).Select(n => n.Notice).ToList();
            }
        }
    }

    // Registering notifies the guild (state 1, 0x2b with the member); two waiting guilds are matched and can join.
    [Test]
    public void Lookup_RegisterMatchJoin() {
        var test = new LookupTest();
        Assert.That(test.Lookup.Register(1, 1000, "Ann"), Is.EqualTo(GuildError.none));
        ChannelGuildRequest notice = test.Of(1).Single();
        Assert.That((notice.PvpState.State, notice.PvpState.Name, notice.PvpState.Line), Is.EqualTo(((int) GuildPvpState.Registered, "Ann", 0x2b)));
        Assert.That(test.Lookup.Join(1, out _, out _, out _), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));

        test.Lookup.Register(2, 1200, "Bob");
        test.Tick();
        Assert.That(test.Fields.Single(), Is.EqualTo((test.Fields[0].Match, 1L, 2L)));
        Assert.That(test.Lookup.Join(2, out int channel, out int mapId, out int roomId), Is.EqualTo(GuildError.none));
        Assert.That((channel, mapId, roomId), Is.EqualTo((2, 64000001, 55)));
        List<ChannelGuildRequest> notices = test.Of(2);
        Assert.That(notices[^2].PvpMatched.Matched, Is.True);
        Assert.That(notices[^1].PvpState.State, Is.EqualTo((int) GuildPvpState.Matched));
        // In a match: no new registration and no unregistration.
        Assert.That(test.Lookup.Register(1, 1000, "Ann"), Is.EqualTo(GuildError.s_guild_err_cant_during_pvp));
        Assert.That(test.Lookup.Unregister(1, "Ann"), Is.EqualTo(GuildError.s_guild_err_cant_during_pvp));

        // The match ends once: both guilds go back to state 0.
        int matchId = test.Fields[0].Match;
        Assert.That(test.Lookup.End(matchId), Is.Not.Null);
        Assert.That(test.Lookup.End(matchId), Is.Null);
        Assert.That(test.Of(1)[^1].PvpState.State, Is.EqualTo((int) GuildPvpState.None));
        Assert.That(test.Lookup.MatchOf(1), Is.Null);
    }

    // The longest waiting guild takes the closest score.
    [Test]
    public void Lookup_PairsTheClosestScore() {
        var test = new LookupTest();
        test.Lookup.Register(1, 1000, "a");
        test.Lookup.Register(2, 1500, "b");
        test.Lookup.Register(3, 1050, "c");
        test.Tick();
        Assert.That(test.Fields.Select(f => (f.A, f.B)), Is.EqualTo(new[] { (1L, 3L) }));
        Assert.That(test.Lookup.IsWaiting(2), Is.True);
    }

    // A guild nobody matched in the matching time is told 0x28 false and goes back to state 0; closed: refused.
    [Test]
    public void Lookup_TimeoutAndClosed() {
        var test = new LookupTest();
        test.Lookup.Register(1, 1000, "a");
        test.Now = 59_999;
        test.Tick();
        Assert.That(test.Lookup.IsWaiting(1), Is.True);
        test.Now = 60_000;
        test.Tick();
        Assert.That(test.Lookup.IsWaiting(1), Is.False);
        List<ChannelGuildRequest> notices = test.Of(1);
        Assert.That(notices[^2].PvpMatched.Matched, Is.False);
        Assert.That(notices[^1].PvpState.State, Is.EqualTo((int) GuildPvpState.None));

        test.Open = false;
        Assert.That(test.Lookup.Register(1, 1000, "a"), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));
    }

    private static int Matched(LookupTest test, long a, long b) {
        test.Lookup.Register(a, 1000, "a");
        test.Lookup.Register(b, 1000, "b");
        test.Tick();
        return test.Fields[^1].Match;
    }

    // After a match both guilds wait Cooldown before they can register again.
    [Test]
    public void Lookup_RegisterCooldownAfterAMatch() {
        var test = new LookupTest();
        test.Lookup.End(Matched(test, 1, 2));
        test.Now += (long) GuildPvpLookup.Cooldown.TotalMilliseconds - 1;
        Assert.That(test.Lookup.Register(1, 1000, "a"), Is.EqualTo(GuildError.s_guild_search_last_request));
        Assert.That(test.Lookup.IsWaiting(1), Is.False);
        test.Now++;
        Assert.That(test.Lookup.Register(1, 1000, "a"), Is.EqualTo(GuildError.none));
    }

    // The same two guilds are not paired again within PairCooldown; another guild can pair with either.
    [Test]
    public void Lookup_NoRepairWithinPairCooldown() {
        var test = new LookupTest();
        test.Lookup.End(Matched(test, 1, 2));
        test.Now += (long) GuildPvpLookup.Cooldown.TotalMilliseconds;
        test.Lookup.Register(1, 1000, "a");
        test.Lookup.Register(2, 1000, "b");
        test.Tick();
        Assert.That(test.Fields, Has.Count.EqualTo(1));
        Assert.That((test.Lookup.IsWaiting(1), test.Lookup.IsWaiting(2)), Is.EqualTo((true, true)));
        test.Lookup.Register(3, 1500, "c");
        test.Tick();
        Assert.That((test.Fields[^1].A, test.Fields[^1].B), Is.EqualTo((1L, 3L)));
        Assert.That(test.Lookup.IsWaiting(2), Is.True);
    }

    // A disbanded guild leaves the queue and its match.
    [Test]
    public void Lookup_DropLeavesQueueAndMatch() {
        var test = new LookupTest();
        test.Lookup.Register(1, 1000, "a");
        test.Lookup.Drop(1);
        Assert.That(test.Lookup.IsWaiting(1), Is.False);

        int matchId = Matched(test, 2, 3);
        test.Lookup.Drop(2);
        Assert.That(test.Lookup.MatchOf(2), Is.Null);
        Assert.That(test.Lookup.MatchOf(3), Is.Null);
        Assert.That(test.Of(3)[^1].PvpState.State, Is.EqualTo((int) GuildPvpState.None));
        Assert.That(test.Lookup.End(matchId), Is.Null);
    }

    // A match whose field was not made is dropped; both guilds are told 0x28 false.
    [Test]
    public void Lookup_FieldFailedDropsTheMatch() {
        var test = new LookupTest { FieldError = MigrationError.s_move_err_no_server };
        test.Lookup.Register(1, 1000, "a");
        test.Lookup.Register(2, 1000, "b");
        test.Tick();
        Assert.That(test.Lookup.MatchOf(1), Is.Null);
        Assert.That(test.Of(2)[^2].PvpMatched.Matched, Is.False);
    }
    #endregion

    #region Handler
    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Flags) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    private static TestGameSession NewTest(params SeasonDataTable.Entry[] guildSeasons) {
        Dictionary<int, SeasonDataTable.Entry> none = [];
        var test = new TestGameSession();
        test.SetTable(new SeasonDataTable(none, none, none, guildSeasons.ToDictionary(season => season.Id), none, none, none, none, none));
        return test;
    }

    private static void JoinGuild(TestGameSession test, GameSession session) {
        var guild = new Guild(GuildId, "Blue", test.AccountId, test.CharacterId, "Master");
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        Set<GuildManager>(manager, "session", session);
        Set<GuildManager>(manager, "logger", new LoggerConfiguration().CreateLogger());
        Set<GuildManager>(manager, "<Guild>k__BackingField", guild);
        session.Guild = manager;
    }

    private static List<string> Send(TestGameSession test, GameSession session, string hex) {
        test.TakeSent();
        new PvpHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteBytes(Convert.FromHexString(hex))));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static string Hex(ByteWriter writer) => Convert.ToHexString(writer.Buffer, 0, writer.Length);

    // Pvp 1 to 4 without a guild: the guild error s_guild_err_null_guild, nothing to the World.
    [TestCase("01")]
    [TestCase("02")]
    [TestCase("03")]
    [TestCase("04")]
    public void Handler_NoGuild_NullGuild(string request) {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        Assert.That(Send(test, session, request), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_null_guild)) }));
        Assert.That(test.World.GuildRequests, Is.Empty);
    }

    // Pvp 1 and 2 go to the World with the guild; its error comes back as the guild error with its argument.
    [Test]
    public void Handler_RegisterAndUnregister() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        JoinGuild(test, session);
        Assert.That(Send(test, session, "01"), Is.Empty);
        GuildRequest request = test.World.GuildRequests.Single();
        Assert.That((request.RequestorId, request.PvpRegister.GuildId), Is.EqualTo((test.CharacterId, GuildId)));

        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_cant_during_pvp, ErrorArg = 3 };
        Assert.That(Send(test, session, "02"), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_cant_during_pvp, 3)) }));
        Assert.That(test.World.GuildRequests[1].PvpUnregister.GuildId, Is.EqualTo(GuildId));
    }

    // Pvp 3 with no ready match: the World's error.
    [Test]
    public void Handler_JoinWithoutMatch() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        JoinGuild(test, session);
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_isNotVsGameTime };
        Assert.That(Send(test, session, "03"), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_isNotVsGameTime)) }));
        Assert.That(test.World.GuildRequests.Single().PvpJoin.GuildId, Is.EqualTo(GuildId));
    }

    // Pvp 4: the own guild becomes the character's championship guild of the season (Pvp 4 back); outside a season
    // isNotVsGameTime.
    [Test]
    public void Handler_Champion() {
        TestGameSession closed = NewTest();
        GameSession closedSession = closed.Start();
        JoinGuild(closed, closedSession);
        Assert.That(Send(closed, closedSession, "04"), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_isNotVsGameTime)) }));

        TestGameSession test = NewTest(new SeasonDataTable.Entry(9, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1), []));
        GameSession session = test.Start();
        JoinGuild(test, session);
        Assert.That(Send(test, session, "04"), Is.EqualTo(new[] { Hex(PvpPacket.ChampionGuild(GuildId, "Blue")) }));
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        Assert.That((character.ChampionSeasonId, character.ChampionGuildId, character.ChampionGuildName), Is.EqualTo((9, GuildId, "Blue")));
    }
    #endregion
}
