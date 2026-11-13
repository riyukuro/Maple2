using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game;

// The Spring Farm arcade game (arcade 1): the game's rules (SpringFarmGame), the PlayArcade packets (s2c 0xD8) as
// the client's CSceneStateGame::OnPlayArcade and its dialogs read them, the arcadegame.xml and arcadeReward.xml
// readers, the lobby's c2s 0x84 modes 9 and 10, the field's end of the game and the trigger actions. Made-up ids.
public class SpringFarmGameTests {
    #region Rules
    // Each use of an interact object adds its score; an object without a score adds nothing.
    [Test]
    public void Interact_AddsItsScoreEachUse() {
        var game = new SpringFarmGame(3);
        game.SetInteractScore(10001, 50);
        game.SetInteractScore(10002, 0);

        Assert.That(game.Interacted(10001), Is.True);
        Assert.That(game.Interacted(10001), Is.True);
        Assert.That(game.Interacted(10002), Is.False);
        Assert.That(game.Interacted(10003), Is.False);
        Assert.That(game.Score, Is.EqualTo(100));
    }

    // An object that keeps its state when used (Ui, Telescope) scores once per object in the game; another object of
    // the same interact id scores too, and an object that resets (Mesh) scores on each use.
    [Test]
    public void Interact_KeepsStateScoresOncePerObject() {
        var game = new SpringFarmGame(3);
        game.SetInteractScore(10001, 50);

        Assert.That(game.Interacted(10001, "a", keepsState: true), Is.True);
        Assert.That(game.Interacted(10001, "a", keepsState: true), Is.False);
        Assert.That(game.Interacted(10001, "b", keepsState: true), Is.True);
        Assert.That(game.Interacted(10001, "c"), Is.True);
        Assert.That(game.Interacted(10001, "c"), Is.True);
        Assert.That(game.Score, Is.EqualTo(200));
    }

    // The game's players are the characters given at start_game.
    [Test]
    public void Players_OfTheStart() {
        var game = new SpringFarmGame(3, [7, 8]);
        Assert.That(game.Plays(7), Is.True);
        Assert.That(game.Plays(8), Is.True);
        Assert.That(game.Plays(9), Is.False);
        Assert.That(new SpringFarmGame(3).Plays(7), Is.False);
    }

    // The arcade fields are solo instance fields (instancefield Type 1): each entry without an owner or a room is a
    // field of its own, so two players entering get two fields. A room id, an owner's room (guild event), a home and
    // the channel-scaled fields take their own paths.
    [TestCase(InstanceType.solo, 0L, 0, true)]
    [TestCase(InstanceType.solo, 0L, 5, false)]
    [TestCase(InstanceType.GuildEvent, 3L, 0, false)]
    [TestCase(InstanceType.GuildEvent, 0L, 0, true)]
    [TestCase(InstanceType.ugcMap, 0L, 0, false)]
    [TestCase(InstanceType.channelScale, 0L, 0, false)]
    [TestCase(InstanceType.DungeonLobby, 0L, 0, false)]
    public void NewFieldPerEntry(InstanceType type, long ownerId, int roomId, bool expected) {
        Assert.That(FieldEntryRules.NewFieldPerEntry(type, ownerId, roomId), Is.EqualTo(expected));
        // A second entry is a new field again.
        Assert.That(FieldEntryRules.NewFieldPerEntry(type, ownerId, roomId), Is.EqualTo(expected));
    }

    // A spawn's score is given once, when its monsters are all dead; a spawn set again gives it again.
    [Test]
    public void Spawn_GivesItsScoreOnce() {
        var game = new SpringFarmGame(3);
        game.SetSpawnScore(901, 300);

        Assert.That(game.SpawnCleared(901), Is.True);
        Assert.That(game.SpawnCleared(901), Is.False);
        Assert.That(game.SpawnCleared(902), Is.False);
        game.SetSpawnScore(901, 300);
        Assert.That(game.SpawnCleared(901), Is.True);
        Assert.That(game.Score, Is.EqualTo(600));
    }

    // "remain": the whole seconds left times the rate; another type, no rate or no time left gives nothing.
    [TestCase("remain", 10, 45_999L, 450)]
    [TestCase("remain", 10, 999L, 0)]
    [TestCase("remain", 10, -5_000L, 0)]
    [TestCase("remain", 0, 45_000L, 0)]
    [TestCase("", 10, 45_000L, 0)]
    public void TimeScore(string type, int rate, long msLeft, int expected) {
        Assert.That(SpringFarmGame.TimeScore(type, rate, msLeft), Is.EqualTo(expected));
    }

    // The round started last gives its time score once, at its clear; the rounds cleared follow the script.
    [Test]
    public void ClearRound_GivesTheTimeScoreOnce() {
        var game = new SpringFarmGame(3);
        game.StartRound(1, "remain", 10, 60_000, 1_000);

        Assert.That(game.ClearRound(1, 31_000), Is.EqualTo(300));
        Assert.That(game.ClearRound(1, 31_000), Is.EqualTo(0));
        Assert.That(game.RoundsCleared, Is.EqualTo(1));

        // A clear of another round than the open one counts the round, without a time score.
        game.StartRound(2, "remain", 10, 60_000, 40_000);
        Assert.That(game.ClearRound(3, 41_000), Is.EqualTo(0));
        Assert.That(game.RoundsCleared, Is.EqualTo(3));
        Assert.That(game.Score, Is.EqualTo(300));
    }

    // A death takes a life while one is left; after the end nothing changes.
    [Test]
    public void Lives_AndTheEndOnce() {
        var game = new SpringFarmGame(2);
        game.SetInteractScore(10001, 50);

        Assert.That(game.Died(), Is.True);
        Assert.That(game.Died(), Is.True);
        Assert.That(game.Died(), Is.False);
        Assert.That(game.Lives, Is.EqualTo(0));
        Assert.That(game.LifeCount, Is.EqualTo(2));

        Assert.That(game.End(), Is.True);
        Assert.That(game.End(), Is.False);
        Assert.That(game.Interacted(10001), Is.False);
        Assert.That(game.ClearRound(1, 0), Is.EqualTo(0));
        Assert.That(game.Score, Is.EqualTo(0));
    }

    // The row of the rounds cleared, else the most rounds below them; nothing for another arcade.
    [TestCase(0, 0.5f, 100)]
    [TestCase(1, 0.5f, 100)]
    [TestCase(2, 1.5f, 300)]
    [TestCase(9, 3f, 900)]
    public void Reward_RowOfTheRounds(int rounds, float expRate, int meso) {
        Assert.That(SpringFarmGame.RewardOf(RewardTable(), rounds), Is.EqualTo(new SpringFarmGame.Reward(expRate, meso)));
    }

    [Test]
    public void Reward_NoRowNoReward() {
        var table = new ArcadeRewardTable(new Dictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>> {
            [2] = new Dictionary<int, ArcadeRewardTable.Entry> { [0] = new(0, 1f, 10) },
            [SpringFarmGame.ArcadeId] = new Dictionary<int, ArcadeRewardTable.Entry> { [3] = new(3, 1f, 10) },
        });
        Assert.That(SpringFarmGame.RewardOf(table, 2), Is.EqualTo(new SpringFarmGame.Reward(0, 0)));
        Assert.That(table.Of(5, 3), Is.Null);
    }

    private static ArcadeRewardTable RewardTable() {
        return new ArcadeRewardTable(new Dictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>> {
            [SpringFarmGame.ArcadeId] = new Dictionary<int, ArcadeRewardTable.Entry> {
                [0] = new(0, 0.5f, 100),
                [2] = new(2, 1.5f, 300),
                [5] = new(5, 3f, 900),
            },
        });
    }

    // A best is replaced only by a higher score; the season holds start <= time < end.
    [Test]
    public void Best_ReplacesAndSeason() {
        Assert.That(ArcadeBest.Replaces(null, 0), Is.True);
        Assert.That(ArcadeBest.Replaces(new ArcadeBest(1, 500, 0), 501), Is.True);
        Assert.That(ArcadeBest.Replaces(new ArcadeBest(1, 500, 0), 500), Is.False);

        var seasons = new Dictionary<int, SeasonDataTable.Entry> {
            [40001] = new(40001, new DateTime(2026, 1, 1), new DateTime(2026, 1, 8), []),
        };
        Assert.That(ArcadeBest.SeasonAt(seasons, new DateTime(2026, 1, 1)), Is.EqualTo(40001));
        Assert.That(ArcadeBest.SeasonAt(seasons, new DateTime(2026, 1, 8)), Is.EqualTo(0));
    }
    // The score dialog and life bar are open for a player of a game not yet ended.
    [Test]
    public void OpenFor_PlayersUntilTheEnd() {
        var game = new SpringFarmGame(3, [7]);
        Assert.Multiple(() => {
            Assert.That(game.OpenFor(7), Is.True);
            Assert.That(game.OpenFor(8), Is.False);
        });
        game.End();
        Assert.That(game.OpenFor(7), Is.False);
    }
    #endregion

    #region Packets
    private static ByteReader Body(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayArcade));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void Packet_OpenScoreLifeAndClose() {
        ByteReader reader = Body(ArcadePacket.Open(1), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Body(ArcadePacket.Score(1234), 1);
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Body(ArcadePacket.LifeInit(1, 3, 3), 5);
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 1, 3, 3 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Body(ArcadePacket.Life(2, 3), 6);
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 2, 3 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Body(ArcadePacket.CloseScore(), 13);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // UIDarkStreamResultDialog::ReadResultPacket: five ints and a byte count of items.
    [Test]
    public void Packet_Result() {
        ByteReader reader = Body(ArcadePacket.Result(900, 700, 2500, 300, 0, []), 2);
        Assert.That(Enumerable.Range(0, 5).Select(_ => reader.ReadInt()), Is.EqualTo(new[] { 900, 700, 2500, 300, 0 }));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // UIArcadeMainDialog mode 9: the season, then a count of (arcade id, score).
    [Test]
    public void Packet_Scores() {
        ByteReader reader = Body(ArcadePacket.Scores(40001, [new ArcadeBest(1, 900, 0), new ArcadeBest(3, 40, 0)]), 9);
        Assert.That(reader.ReadInt(), Is.EqualTo(40001));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(Enumerable.Range(0, 4).Select(_ => reader.ReadInt()), Is.EqualTo(new[] { 1, 900, 3, 40 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
    #endregion

    #region Readers
    [Test]
    public void ArcadeGameRows() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <arcadeGame id="1" feature="" sortingOrder="2" arcadeBanner="a" targetField="51000001" />
              <arcadeGame id="2" feature="" sortingOrder="1" arcadeBanner="b" targetField="51000002" isEventArcade="1" />
              <arcadeGame id="3" feature="Off" sortingOrder="3" arcadeBanner="c" targetField="51000003" />
            </ms2>
            """);
        Dictionary<int, ArcadeGameTable.Entry> rows = TableMapper.ReadArcadeGames(document, node => node.GetAttribute("feature") == "");
        Assert.That(rows.Keys, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(rows[1], Is.EqualTo(new ArcadeGameTable.Entry(1, 2, 51000001, false)));
        Assert.That(rows[2], Is.EqualTo(new ArcadeGameTable.Entry(2, 1, 51000002, true)));
    }

    // A row of the locale replaces the row without one for the same round, before or after it; filtered rows are
    // left out.
    [Test]
    public void ArcadeRewardRows() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <arcadeGame arcadeId="1">
                <reward round="0" expRate="0.5" meso="100" />
                <reward round="0" expRate="0.75" meso="150" locale="XX" />
                <reward round="1" expRate="2" meso="400" locale="XX" />
                <reward round="1" expRate="1" meso="200" />
                <reward round="2" expRate="9" meso="900" locale="YY" />
              </arcadeGame>
            </ms2>
            """);
        ArcadeRewardTable table = ServerTableMapper.MapArcadeReward(document, row => row.GetAttribute("locale") is "" or "XX");
        Assert.That(table.Entries[1].Values, Is.EquivalentTo(new[] {
            new ArcadeRewardTable.Entry(0, 0.75f, 150),
            new ArcadeRewardTable.Entry(1, 2f, 400),
        }));
    }

    // A second node of the same arcade adds its rows to the first one's instead of replacing them; its row of a round
    // the first has replaces it, with the same locale rule. The shipped file has one node per arcade (ids 1-5).
    [Test]
    public void ArcadeRewardRows_RepeatedArcade() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <arcadeGame arcadeId="1">
                <reward round="0" expRate="0.5" meso="100" />
                <reward round="1" expRate="1" meso="200" locale="XX" />
              </arcadeGame>
              <arcadeGame arcadeId="2">
                <reward round="0" expRate="3" meso="30" />
              </arcadeGame>
              <arcadeGame arcadeId="1">
                <reward round="1" expRate="7" meso="700" />
                <reward round="2" expRate="2" meso="300" />
                <reward round="0" expRate="0.25" meso="50" />
              </arcadeGame>
            </ms2>
            """);
        ArcadeRewardTable table = ServerTableMapper.MapArcadeReward(document, row => row.GetAttribute("locale") is "" or "XX");
        Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(table.Entries[1].Values, Is.EquivalentTo(new[] {
            new ArcadeRewardTable.Entry(0, 0.25f, 50),
            new ArcadeRewardTable.Entry(1, 1f, 200),
            new ArcadeRewardTable.Entry(2, 2f, 300),
        }));
        Assert.That(table.Entries[2].Values, Is.EquivalentTo(new[] { new ArcadeRewardTable.Entry(0, 3f, 30) }));
    }
    #endregion

    #region Other arcades
    // Three Two One: the players win a round when every game player in the field picked a direction other than the
    // NPC's; a pick of the NPC's direction, a missing pick, a pick after result_round and a non-player's pick lose.
    [Test]
    public void ThreeTwoOne_RoundRules() {
        var game = new ThreeTwoOneGame(3, 5, 10000, [7, 8]);
        Assert.That(game.Score, Is.EqualTo(10000));
        game.StartRound(1);
        Assert.That(game.Pick(7, 1), Is.True);
        Assert.That(game.Pick(8, 2), Is.True);
        Assert.That(game.Pick(9, 2), Is.False);
        Assert.That(game.ResultRound(3, [7, 8, 9]), Is.True);
        Assert.That(game.Pick(7, 3), Is.False);
        Assert.That(game.ResultRound2(), Is.False);
        Assert.That(game.ResultRound2(), Is.False);
        Assert.That((game.Score, game.Streak, game.Lives), Is.EqualTo((11000, 1, 5)));

        game.StartRound(2);
        game.Pick(7, 1);
        game.Pick(8, 3);
        Assert.That(game.ResultRound(3, [7, 8]), Is.False);
        Assert.That(game.ResultRound2(), Is.True);
        Assert.That((game.Score, game.Streak, game.Lives), Is.EqualTo((11000, 0, 4)));

        // A player who left the field does not count; one still in it without a pick loses the round.
        game.StartRound(3);
        game.Pick(7, 2);
        Assert.That(game.ResultRound(1, [7]), Is.True);
        game.StartRound(4);
        Assert.That(game.ResultRound(1, [7, 8]), Is.False);
        // Nobody of the game in the field: lost.
        game.StartRound(5);
        Assert.That(game.ResultRound(1, []), Is.False);
        Assert.That(ThreeTwoOneGame.IsArcade(2), Is.False);
        Assert.That(ThreeTwoOneGame.IsArcade(4), Is.True);
    }

    // Boom Boom Ocean: the score stays at 0 or above; a negative score takes a life, a positive one does not; the
    // round's time score is given once.
    [Test]
    public void BoomBoomOcean_ScoreRules() {
        var game = new BoomBoomOceanGame(2);
        game.SetSkillScore(1, -500);
        game.SetSkillScore(2, 1000);
        Assert.That(game.Scored(1, out bool lost), Is.True);
        Assert.That((game.Score, lost, game.Lives), Is.EqualTo((0, true, 1)));
        Assert.That(game.Scored(2, out lost), Is.True);
        Assert.That((game.Score, lost, game.Lives), Is.EqualTo((1000, false, 1)));
        Assert.That(game.Scored(3, out _), Is.False);

        game.StartRound(1, 1500, 1033, 0);
        Assert.That(game.ClearRound(1, 20_000), Is.EqualTo(1033));
        Assert.That(game.ClearRound(1, 20_000), Is.EqualTo(0));
        Assert.That(BoomBoomOceanGame.TimeScore(10, 999), Is.EqualTo(0));
    }

    // A hit scores when it lands: an evaded hit (Miss) or one on an invincible player does not; a block still
    // lands, as does a hit without damage rows.
    [TestCase(new[] { DamageType.Normal }, true)]
    [TestCase(new[] { DamageType.Critical }, true)]
    [TestCase(new[] { DamageType.Block }, true)]
    [TestCase(new[] { DamageType.Miss }, false)]
    [TestCase(new[] { DamageType.Invincible }, false)]
    [TestCase(new[] { DamageType.InvincibleReflection }, false)]
    [TestCase(new[] { DamageType.Miss, DamageType.Normal }, true)]
    [TestCase(new DamageType[0], true)]
    public void BoomBoomOcean_HitLands(DamageType[] hits, bool expected) {
        Assert.That(BoomBoomOceanGame.HitLands(hits), Is.EqualTo(expected));
    }

    // A killing hit that took a life: the death after it takes none, the next death does.
    [Test]
    public void BoomBoomOcean_KillingHitTakesOneLife() {
        var game = new BoomBoomOceanGame(3, [7]);
        game.SetSkillScore(1, -500);
        Assert.That(game.Scored(1, out bool lost), Is.True);
        Assert.That(lost, Is.True);
        game.KillingHitTookLife(7);
        Assert.That(game.Died(7), Is.False);
        Assert.That(game.Lives, Is.EqualTo(2));
        Assert.That(game.Died(7), Is.True);
        Assert.That(game.Lives, Is.EqualTo(1));
    }

    // Three Two One: the rounds cleared are the rounds won; the script's clear_round (round 1 in every shipped
    // script, after a loss too) changes nothing.
    [Test]
    public void ThreeTwoOne_RoundsClearedAreWins() {
        var game = new ThreeTwoOneGame(5, 3, 10000, [7]);
        game.StartRound(1);
        game.ResultRound(1, [7]);
        game.ResultRound2();
        game.ClearRound(1);
        Assert.That(game.RoundsCleared, Is.EqualTo(0));
        for (int round = 2; round <= 3; round++) {
            game.StartRound(round);
            game.Pick(7, 2);
            game.ResultRound(1, [7]);
            game.ResultRound2();
            game.ClearRound(1);
        }
        Assert.That((game.Wins, game.RoundsCleared), Is.EqualTo((2, 2)));
    }

    // The reward row is the arcade's own.
    [Test]
    public void Reward_OfTheArcade() {
        var table = new ArcadeRewardTable(new Dictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>> {
            [1] = new Dictionary<int, ArcadeRewardTable.Entry> { [0] = new(0, 0.5f, 100) },
            [2] = new Dictionary<int, ArcadeRewardTable.Entry> { [0] = new(0, 1.5f, 200) },
        });
        Assert.That(new BoomBoomOceanGame(1).RewardOf(table), Is.EqualTo(new ArcadeGame.Reward(1.5f, 200)));
        Assert.That(new SpringFarmGame(1).RewardOf(table), Is.EqualTo(new ArcadeGame.Reward(0.5f, 100)));
        Assert.That(new ThreeTwoOneGame(3, 1, 0).RewardOf(table), Is.EqualTo(new ArcadeGame.Reward(0, 0)));
    }
    #endregion
}

// The lobby's c2s 0x84 (PlayArcadeHandler), the field's end of the game and the trigger actions, on a
// TestGameSession.
public class SpringFarmSessionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int GameField = 51000001;
    private const int EventField = 51000002;
    private const int Season = 40001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private PlayArcadeHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new ArcadeGameTable(new Dictionary<int, ArcadeGameTable.Entry> {
            [1] = new(1, 1, GameField, false),
            [2] = new(2, 2, EventField, true),
            [3] = new(3, 3, 0, false),
        }));
        SetSeason(DateTime.Now.AddDays(-1), DateTime.Now.AddDays(6));
        // Arcade exp: 100 at every level.
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.arcade] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetServerTable(new ArcadeRewardTable(new Dictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>> {
            [SpringFarmGame.ArcadeId] = new Dictionary<int, ArcadeRewardTable.Entry> {
                [0] = new(0, 0.5f, 100),
                [2] = new(2, 2.5f, 300),
            },
            [BoomBoomOceanGame.ArcadeId] = new Dictionary<int, ArcadeRewardTable.Entry> {
                [3] = new(3, 1f, 70),
            },
            [5] = new Dictionary<int, ArcadeRewardTable.Entry> {
                [0] = new(0, 0f, 10),
                [1] = new(1, 0f, 50),
                [2] = new(2, 0f, 90),
            },
        }));
        test.Start();
        typeof(FieldManager).GetField("<GameStorage>k__BackingField", Any)!.SetValue(session.Field, test.Storage);
        handler = new PlayArcadeHandler { GameStorage = test.Storage };
        test.TakeSent();
    }

    private void SetSeason(DateTime start, DateTime end) {
        var empty = new Dictionary<int, SeasonDataTable.Entry>();
        test.SetTable(new SeasonDataTable(new Dictionary<int, SeasonDataTable.Entry> { [Season] = new(Season, start, end, []) },
            empty, empty, empty, empty, empty, empty, empty, empty));
    }

    private void Send(byte mode, int? arcadeId = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            if (arcadeId != null) {
                writer.WriteInt(arcadeId.Value);
            } else {
                // Encode4(9): the mode and three zero bytes.
                writer.WriteByte(0);
                writer.WriteShort(0);
            }
        }));
    }

    private List<SendOp> Sent() {
        List<SendOp> ops = test.TakeSentOps();
        Assert.That(test.Errors, Is.Empty);
        return ops;
    }

    private static ByteReader Arcade(byte[] packet, byte mode) {
        var reader = new ByteReader(packet, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayArcade));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    // The end's packets close with the life bar's close (HideAndSeek 3, the player's object): the result is the one
    // before it.
    private byte[] ResultOf(List<byte[]> sent) {
        Assert.That(sent, Has.Count.GreaterThanOrEqualTo(2));
        var close = new ByteReader(sent[^1], 0);
        Assert.That(close.Read<SendOp>(), Is.EqualTo(SendOp.HideAndSeek));
        Assert.That(close.ReadByte(), Is.EqualTo(3));
        Assert.That(close.ReadInt(), Is.EqualTo(session.Player.ObjectId));
        Assert.That(close.Available, Is.EqualTo(0));
        return sent[^2];
    }

    private void SaveBest(int arcadeId, int season, int score) {
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.SaveArcadeScore(test.CharacterId, arcadeId, season, score, 1_800_000_000), Is.True);
    }

    private ArcadeBest? Best(int season = Season) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetArcadeBest(test.CharacterId, SpringFarmGame.ArcadeId, season);
    }

    #region Lobby (c2s 0x84)
    // Mode 9: the current season and the player's bests in it, by arcade id; another season's are not listed.
    [Test]
    public void Scores_OfTheCurrentSeason() {
        SaveBest(3, Season, 40);
        SaveBest(1, Season, 900);
        SaveBest(1, Season + 1, 5000);

        Send(PlayArcadeHandler.Scores);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.PlayArcade }));
        ByteReader reader = Arcade(sent[0], 9);
        Assert.That(reader.ReadInt(), Is.EqualTo(Season));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(Enumerable.Range(0, 4).Select(_ => reader.ReadInt()), Is.EqualTo(new[] { 1, 900, 3, 40 }));
        Assert.That(test.Errors, Is.Empty);
    }

    // Without a current season the lobby gets season 0 and no scores.
    [Test]
    public void Scores_NoSeason() {
        SaveBest(1, Season, 900);
        SetSeason(DateTime.Now.AddDays(-8), DateTime.Now.AddDays(-1));

        Send(PlayArcadeHandler.Scores);

        List<byte[]> sent = test.TakeSent();
        ByteReader reader = Arcade(sent.Single(), 9);
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
    }

    // Mode 10 moves the player to the game's field. The test field's map is one the played client lacks, so the
    // move stops at the map check (its notice) and the lobby gets the move error: the request reached PrepareField
    // with the game's field.
    [Test]
    public void Enter_GoesToTheGamesField() {
        MapMetadata map = TestGameSession.Map(GameField, notInPlayedClient: true);
        test.AddMap(map with { Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)) });

        Send(PlayArcadeHandler.Enter, 1);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Notice, SendOp.RequestFieldEnter }));
        Assert.That(sent[1][2], Is.EqualTo((byte) Maple2.Model.Error.MigrationError.s_move_err_default));
        Assert.That(test.Errors, Is.Empty);
    }

    // Dropped: a game the table lacks, an event arcade, a game without a field, the player already in the field,
    // and a dead player.
    [Test]
    public void Enter_Refused() {
        Send(PlayArcadeHandler.Enter, 9);
        Send(PlayArcadeHandler.Enter, 2);
        Send(PlayArcadeHandler.Enter, 3);
        Assert.That(Sent(), Is.Empty);

        test.EnterMap(TestGameSession.Map(GameField));
        Send(PlayArcadeHandler.Enter, 1);
        Assert.That(Sent(), Is.Empty);

        test.EnterMap(TestGameSession.Map(2000001));
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(session.Player, true);
        Send(PlayArcadeHandler.Enter, 1);
        Assert.That(Sent(), Is.Empty);
    }

    // In battle, as the client's lobby refuses it: dropped.
    [Test]
    public void Enter_RefusedInBattle() {
        MapMetadata map = TestGameSession.Map(GameField, notInPlayedClient: true);
        test.AddMap(map with { Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)) });
        typeof(FieldPlayer).GetField("inBattle", Any)!.SetValue(session.Player, true);

        Send(PlayArcadeHandler.Enter, 1);

        Assert.That(Sent(), Is.Empty);
    }
    #endregion

    #region Field
    private SpringFarmGame StartGame(int lives = 2, long? player = null) {
        var game = new SpringFarmGame(lives, [player ?? test.CharacterId]);
        session.Field!.SpringFarm = game;
        return game;
    }

    // An interact object with a score sends the new score; one without sends nothing.
    [Test]
    public void Interact_SendsTheScore() {
        SpringFarmGame game = StartGame();
        game.SetInteractScore(10001, 50);

        session.Field!.SpringFarmInteracted(10002);
        Assert.That(Sent(), Is.Empty);

        session.Field.SpringFarmInteracted(10001);
        List<byte[]> sent = test.TakeSent();
        Assert.That(Arcade(sent.Single(), 1).ReadInt(), Is.EqualTo(50));
    }

    // Without a game in the field nothing happens.
    [Test]
    public void NoGame_Nothing() {
        session.Field!.SpringFarmInteracted(10001);
        session.Field.SpringFarmDied();
        session.Field.EndSpringFarm();
        Assert.That(Sent(), Is.Empty);
    }

    // The end: the exp curve times the row's rate, the row's meso, the best kept, then the result with the best
    // before the game.
    [Test]
    public void End_GivesTheRowAndKeepsTheBest() {
        SaveBest(1, Season, 400);
        SpringFarmGame game = StartGame();
        game.SetInteractScore(10001, 500);
        game.Interacted(10001);
        game.StartRound(2, "", 0, 0, 0);
        game.ClearRound(2, 0);
        long meso = session.Currency.Meso;
        long exp = session.Player.Value.Character.Exp;
        test.TakeSent();

        session.Field!.EndSpringFarm();

        Assert.That(session.Player.Value.Character.Exp - exp, Is.EqualTo(250));
        Assert.That(session.Currency.Meso - meso, Is.EqualTo(300));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ExpUp, SendOp.Meso, SendOp.PlayArcade, SendOp.HideAndSeek }));
        ByteReader reader = Arcade(ResultOf(sent), 2);
        Assert.That(Enumerable.Range(0, 5).Select(_ => reader.ReadInt()), Is.EqualTo(new[] { 500, 400, 250, 300, 0 }));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(Best()!.Score, Is.EqualTo(500));
        Assert.That(test.Errors, Is.Empty);

        // Once.
        session.Field.EndSpringFarm();
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // A lower score keeps the old best and shows it.
    [Test]
    public void End_LowerScoreKeepsTheBest() {
        SaveBest(1, Season, 900);
        StartGame();
        test.TakeSent();

        session.Field!.EndSpringFarm();

        byte[] result = ResultOf(test.TakeSent());
        ByteReader reader = Arcade(result, 2);
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(900));
        Assert.That(Best()!.Score, Is.EqualTo(900));
    }

    // The end pays only the game's players: a character in the field that was not in it at start_game gets no exp,
    // no meso, no best and no result.
    [Test]
    public void End_PaysOnlyTheGamesPlayers() {
        SpringFarmGame game = StartGame(player: test.CharacterId + 1);
        game.SetInteractScore(10001, 500);
        game.Interacted(10001);
        long meso = session.Currency.Meso;
        long exp = session.Player.Value.Character.Exp;
        test.TakeSent();

        session.Field!.EndSpringFarm();

        Assert.That(game.Ended, Is.True);
        Assert.That(Sent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(exp));
        Assert.That(Best(), Is.Null);
    }

    // Without an arcade season the end pays and shows the result, and keeps no best (season 0).
    [Test]
    public void End_NoSeasonKeepsNoBest() {
        SetSeason(DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
        SpringFarmGame game = StartGame();
        game.SetInteractScore(10001, 500);
        game.Interacted(10001);
        test.TakeSent();

        session.Field!.EndSpringFarm();

        ByteReader reader = Arcade(ResultOf(test.TakeSent()), 2);
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 500, 0 }));
        Assert.That(Best(0), Is.Null);
        Assert.That(Best(), Is.Null);
    }

    // A death sends the life bar; the last life ends the game with the result.
    [Test]
    public void Death_TakesALifeAndTheLastEndsTheGame() {
        StartGame(lives: 2);

        session.Field!.SpringFarmDied();
        List<byte[]> sent = test.TakeSent();
        ByteReader reader = Arcade(sent.Single(), 6);
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 1, 2 }));

        session.Field.SpringFarmDied();
        sent = test.TakeSent();
        Assert.That(Arcade(sent[0], 6).ReadInt(), Is.EqualTo(0));
        Arcade(ResultOf(sent), 2);
        Assert.That(session.Field.SpringFarm!.Ended, Is.True);

        session.Field.SpringFarmDied();
        Assert.That(test.TakeSent(), Is.Empty);
    }
    // A game player who leaves before the end: the score dialog closes (mode 13, no payload) and the life bar with
    // HideAndSeek 3 for the player's object. After the end, or for a character not in the game, nothing goes.
    [Test]
    public void Leave_ClosesTheScoreAndLifeBarBeforeTheEnd() {
        SpringFarmGame game = StartGame();
        session.Field!.ArcadeLeave(session.Player);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        ByteReader close = Arcade(sent[0], 13);
        Assert.That(close.Available, Is.EqualTo(0));
        Assert.That(ResultOf(sent), Is.SameAs(sent[0]));

        game.End();
        session.Field.ArcadeLeave(session.Player);
        Assert.That(Sent(), Is.Empty);

        StartGame(player: test.CharacterId + 1);
        session.Field.ArcadeLeave(session.Player);
        Assert.That(Sent(), Is.Empty);
    }
    #endregion

    #region Triggers
    private const int TriggerId = 9000;

    private TriggerContext Context() {
        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        GC.SuppressFinalize(owner);
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(owner, session.Field);
        typeof(FieldEntity<TriggerModel>).GetField("<Value>k__BackingField", Any)!.SetValue(owner, new TriggerModel(TriggerId, "main", Vector3.Zero, Vector3.Zero));
        // The test field is built without its initializers: the trigger user values the Three Two One result sets,
        FieldInfo userValues = typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!;
        if (userValues.GetValue(session.Field) == null) {
            userValues.SetValue(session.Field, new TriggerUserValues());
        }
        // and the portals the last life lost looks up (none: the players stay).
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", Any)!;
        if (portals.GetValue(session.Field) == null) {
            portals.SetValue(session.Field, new System.Collections.Concurrent.ConcurrentDictionary<int, FieldPortal>());
        }
        var context = (TriggerContext) RuntimeHelpers.GetUninitializedObject(typeof(TriggerContext));
        typeof(TriggerContext).GetField("owner", Any)!.SetValue(context, owner);
        return context;
    }

    private static void Run(TriggerContext context, string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes).Execute(context);
    }

    private void SetTick(long tick) {
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(session.Field, tick);
    }

    // The shipped script's actions: start_game opens the score dialog (type 1) and the life bar, start_round reads
    // round, ui_duration, time_score_type, time_score_rate and round_duration by name, clear_round adds the time
    // score, end_game gives the result.
    [Test]
    public void Script_StartRoundClearEnd() {
        TriggerContext context = Context();
        SetTick(10_000);

        Run(context, """<action name="arcade_spring_farm_start_game" life_count="3" />""");
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(Arcade(sent[0], 0).ReadInt(), Is.EqualTo(1));
        ByteReader life = Arcade(sent[1], 5);
        Assert.That(new[] { life.ReadInt(), life.ReadInt(), life.ReadInt() }, Is.EqualTo(new[] { 1, 3, 3 }));

        Run(context, """<action name="arcade_spring_farm_set_interact_score" id="10001" score="70" />""");
        Run(context, """<action name="arcade_spring_farm_start_round" round="1" ui_duration="3000" time_score_type="remain" time_score_rate="10" round_duration="60000" />""");
        SpringFarmGame game = session.Field!.SpringFarm!;
        Assert.That(game.Plays(test.CharacterId), Is.True);
        Assert.That(game.Round, Is.EqualTo(1));
        session.Field.SpringFarmInteracted(10001);
        test.TakeSent();

        // 20 s in: 40 s left at 10 a second.
        SetTick(30_000);
        Run(context, """<action name="arcade_spring_farm_clear_round" round="1" />""");
        sent = test.TakeSent();
        Assert.That(Arcade(sent.Single(), 1).ReadInt(), Is.EqualTo(470));
        Assert.That(game.RoundsCleared, Is.EqualTo(1));

        Run(context, """<action name="arcade_spring_farm_end_game" />""");
        sent = test.TakeSent();
        Assert.That(Arcade(ResultOf(sent), 2).ReadInt(), Is.EqualTo(470));
        Assert.That(game.Ended, Is.True);
        Assert.That(test.Errors, Is.Empty);
    }

    private void Pick(int direction) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(PlayArcadeHandler.Pick);
            writer.WriteInt(direction);
        }));
    }

    private int ThreeTwoOneResult() => session.Field!.UserValues.Get(TriggerId, TriggerContext.ThreeTwoOneResultKey);

    // The Three Two One script (field 51000005, arcade 4): start_game opens the score dialog at the starting score
    // and the life bar; start_round (round, then ui_duration, by name) shows the buttons; the c2s pick and
    // result_round set ThreeTwoOneResult; result_round2 adds a won round's score and shows the wins in a row.
    [Test]
    public void ThreeTwoOne_ScriptRound() {
        TriggerContext context = Context();
        Run(context, """<action name="arcade_three_two_one2_start_game" type="StartGame" life_count="2" init_score="10000" />""");
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(3));
        Assert.That(Arcade(sent[0], 0).ReadInt(), Is.EqualTo(4));
        Assert.That(Arcade(sent[1], 1).ReadInt(), Is.EqualTo(10000));
        ByteReader life = Arcade(sent[2], 5);
        Assert.That(new[] { life.ReadInt(), life.ReadInt(), life.ReadInt() }, Is.EqualTo(new[] { 1, 2, 2 }));
        ThreeTwoOneGame game = session.Field!.ThreeTwoOne!;
        Assert.That(game.Plays(test.CharacterId), Is.True);

        Run(context, """<action name="arcade_three_two_one2_start_round" type="StartRound" round="1" ui_duration="4" />""");
        Assert.That(game.Round, Is.EqualTo(1));
        ByteReader buttons = Arcade(test.TakeSent().Single(), 7);
        Assert.That(buttons.ReadBool(), Is.True);
        Assert.That(buttons.ReadInt(), Is.EqualTo(4));
        Assert.That(buttons.Available, Is.EqualTo(0));

        Pick(2);
        Run(context, """<action name="arcade_three_two_one2_result_round" type="ResultRound" result_direction="1" />""");
        ByteReader hide = Arcade(test.TakeSent().Single(), 7);
        Assert.That(hide.ReadBool(), Is.False);
        Assert.That(hide.Available, Is.EqualTo(0));
        Assert.That(ThreeTwoOneResult(), Is.EqualTo(1));

        Run(context, """<action name="arcade_three_two_one2_result_round2" type="ResultRound2" round="1" />""");
        sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(Arcade(sent[0], 1).ReadInt(), Is.EqualTo(11000));
        ByteReader streak = Arcade(sent[1], 8);
        Assert.That(streak.ReadByte(), Is.EqualTo(0));
        Assert.That(streak.ReadInt(), Is.EqualTo(1));
        Run(context, """<action name="arcade_three_two_one2_clear_round" type="ClearRound" round="1" />""");
        Assert.That(game.RoundsCleared, Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // A round lost (the NPC's direction picked, or no pick) takes a life and ends the streak; the last life ends the
    // game with the result and the life bar's close.
    [Test]
    public void ThreeTwoOne_LossesEndTheGame() {
        TriggerContext context = Context();
        Run(context, """<action name="arcade_three_two_one_start_game" type="StartGame" life_count="2" init_score="10000" />""");
        Run(context, """<action name="arcade_three_two_one_start_round" type="StartRound" round="1" ui_duration="4" />""");
        Pick(3);
        Run(context, """<action name="arcade_three_two_one_result_round" type="ResultRound" result_direction="3" />""");
        Assert.That(ThreeTwoOneResult(), Is.EqualTo(0));
        test.TakeSent();
        Run(context, """<action name="arcade_three_two_one_result_round2" type="ResultRound2" round="1" />""");
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        ByteReader streak = Arcade(sent[0], 8);
        Assert.That(new[] { (int) streak.ReadByte(), streak.ReadInt() }, Is.EqualTo(new[] { 0, 0 }));
        ByteReader life = Arcade(sent[1], 6);
        Assert.That(new[] { life.ReadInt(), life.ReadInt() }, Is.EqualTo(new[] { 1, 2 }));

        // No pick: lost.
        Run(context, """<action name="arcade_three_two_one_start_round" type="StartRound" round="2" ui_duration="4" />""");
        Run(context, """<action name="arcade_three_two_one_result_round" type="ResultRound" result_direction="1" />""");
        Assert.That(ThreeTwoOneResult(), Is.EqualTo(0));
        test.TakeSent();
        Run(context, """<action name="arcade_three_two_one_result_round2" type="ResultRound2" round="2" />""");
        sent = test.TakeSent();
        Assert.That(Arcade(sent[1], 6).ReadInt(), Is.EqualTo(0));
        ByteReader result = Arcade(ResultOf(sent), 2);
        Assert.That(new[] { result.ReadInt(), result.ReadInt(), result.ReadInt(), result.ReadInt() }, Is.EqualTo(new[] { 10000, 0, 0, 0 }));
        Assert.That(session.Field!.ThreeTwoOne!.Ended, Is.True);

        // The fail script's end_game after it: nothing more.
        Run(context, """<action name="arcade_three_two_one_end_game" type="EndGame" />""");
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // A pick outside 1 to 3, or before a round opens, is dropped; without a game the pick does nothing.
    [Test]
    public void ThreeTwoOne_PickDropped() {
        Pick(1);
        Assert.That(Sent(), Is.Empty);
        TriggerContext context = Context();
        Run(context, """<action name="arcade_three_two_one3_start_game" type="StartGame" life_count="5" init_score="10000" />""");
        Assert.That(Arcade(test.TakeSent()[0], 0).ReadInt(), Is.EqualTo(5));
        Run(context, """<action name="arcade_three_two_one3_start_round" type="StartRound" round="1" ui_duration="4" />""");
        Pick(4);
        Run(context, """<action name="arcade_three_two_one3_result_round" type="ResultRound" result_direction="1" />""");
        Assert.That(ThreeTwoOneResult(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // The Boom Boom Ocean script (field 51000003, arcade 2): start_game opens the score dialog (type 2) and the life
    // bar; a scored skill's hit on a game player takes its score and a life, a scored buff adds its score; end_game
    // gives arcade 2's reward row of the rounds cleared, and the round still open pays no time score.
    [Test]
    public void BoomBoomOcean_Script() {
        TriggerContext context = Context();
        SetTick(10_000);
        Run(context, """<action name="arcade_boom_boom_ocean_start_game" life_count="20" />""");
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(Arcade(sent[0], 0).ReadInt(), Is.EqualTo(2));
        ByteReader life = Arcade(sent[1], 5);
        Assert.That(new[] { life.ReadInt(), life.ReadInt(), life.ReadInt() }, Is.EqualTo(new[] { 1, 20, 20 }));
        Run(context, """<action name="arcade_boom_boom_ocean_set_skill_score" id="49210001" score="-500" />""");
        Run(context, """<action name="arcade_boom_boom_ocean_set_skill_score" id="70000080" score="1000" />""");
        BoomBoomOceanGame game = session.Field!.BoomBoomOcean!;

        Run(context, """<action name="arcade_boom_boom_ocean_start_round" round="3" round_duration="20000" time_score_rate="10" />""");
        session.Field.BoomBoomOceanScored(session.Player, 70000080);
        Assert.That(Arcade(test.TakeSent().Single(), 1).ReadInt(), Is.EqualTo(1000));
        session.Field.BoomBoomOceanScored(session.Player, 49210001);
        sent = test.TakeSent();
        Assert.That(Arcade(sent[0], 1).ReadInt(), Is.EqualTo(500));
        ByteReader lost = Arcade(sent[1], 6);
        Assert.That(new[] { lost.ReadInt(), lost.ReadInt() }, Is.EqualTo(new[] { 19, 20 }));
        session.Field.BoomBoomOceanScored(session.Player, 12345);
        Assert.That(Sent(), Is.Empty);

        // 12.5 s in: 12 whole seconds at 10.
        SetTick(22_500);
        Run(context, """<action name="arcade_boom_boom_ocean_clear_round" round="3" />""");
        Assert.That(Arcade(test.TakeSent().Single(), 1).ReadInt(), Is.EqualTo(620));
        Assert.That(game.RoundsCleared, Is.EqualTo(3));

        // The next round is still open at the end (the fail box): its time is not paid.
        Run(context, """<action name="arcade_boom_boom_ocean_start_round" round="4" round_duration="20000" time_score_rate="10" />""");
        SetTick(42_000);
        long meso = session.Currency.Meso;
        Run(context, """<action name="arcade_boom_boom_ocean_end_game" />""");
        sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ExpUp, SendOp.Meso, SendOp.PlayArcade, SendOp.HideAndSeek }));
        ByteReader result = Arcade(ResultOf(sent), 2);
        Assert.That(Enumerable.Range(0, 4).Select(_ => result.ReadInt()), Is.EqualTo(new[] { 620, 0, 100, 70 }));
        Assert.That(session.Currency.Meso - meso, Is.EqualTo(70));
        Assert.That(game.Ended, Is.True);
        Assert.That(test.Errors, Is.Empty);
    }

    // Boom Boom Ocean: a death takes a life; a hit on a character not in the game scores nothing.
    [Test]
    public void BoomBoomOcean_DeathAndOtherPlayer() {
        var game = new BoomBoomOceanGame(2, [test.CharacterId]);
        game.SetSkillScore(49210001, -500);
        session.Field!.BoomBoomOcean = game;
        session.Field.BoomBoomOceanDied(session.Player);
        ByteReader life = Arcade(test.TakeSent().Single(), 6);
        Assert.That(new[] { life.ReadInt(), life.ReadInt() }, Is.EqualTo(new[] { 1, 2 }));

        session.Field.BoomBoomOcean = new BoomBoomOceanGame(2, [test.CharacterId + 1]);
        session.Field.BoomBoomOcean.SetSkillScore(49210001, -500);
        session.Field.BoomBoomOceanScored(session.Player, 49210001);
        session.Field.BoomBoomOceanDied(session.Player);
        Assert.That(Sent(), Is.Empty);
    }

    // A lethal hit of a negative score takes one life: the death that follows takes none.
    [Test]
    public void BoomBoomOcean_LethalHitOneLife() {
        var game = new BoomBoomOceanGame(3, [test.CharacterId]);
        game.SetSkillScore(49210001, -500);
        session.Field!.BoomBoomOcean = game;
        session.Field.BoomBoomOceanScored(session.Player, 49210001, killed: true);
        session.Field.BoomBoomOceanDied(session.Player);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        ByteReader life = Arcade(sent[1], 6);
        Assert.That(new[] { life.ReadInt(), life.ReadInt() }, Is.EqualTo(new[] { 2, 3 }));
        Assert.That(game.Lives, Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // The last life lost inside a round ends the game without that round's time score.
    [Test]
    public void BoomBoomOcean_LastLifeInsideARoundPaysNoTime() {
        var game = new BoomBoomOceanGame(1, [test.CharacterId]);
        session.Field!.BoomBoomOcean = game;
        Context();
        SetTick(30_000);
        game.StartRound(1, 20_000, 10, 10_000);
        session.Field.BoomBoomOceanDied(session.Player);
        ByteReader result = Arcade(ResultOf(test.TakeSent()), 2);
        Assert.That(result.ReadInt(), Is.EqualTo(0));
        Assert.That(game.Ended, Is.True);
        Assert.That(test.Errors, Is.Empty);
    }

    // Three Two One: a lost round's clear_round (round 1 in the shipped scripts) clears nothing, so the end gives
    // the row of no rounds; two wins give the row of two.
    [TestCase(false, 10)]
    [TestCase(true, 90)]
    public void ThreeTwoOne_RewardRowOfTheWins(bool win, int meso) {
        TriggerContext context = Context();
        Run(context, """<action name="arcade_three_two_one3_start_game" type="StartGame" life_count="5" init_score="10000" />""");
        for (int round = 1; round <= 2; round++) {
            Run(context, $"""<action name="arcade_three_two_one3_start_round" type="StartRound" round="{round}" ui_duration="4" />""");
            Pick(win ? 2 : 1);
            Run(context, """<action name="arcade_three_two_one3_result_round" type="ResultRound" result_direction="1" />""");
            Run(context, """<action name="arcade_three_two_one3_result_round2" type="ResultRound2" round="1" />""");
            Run(context, """<action name="arcade_three_two_one3_clear_round" type="ClearRound" round="1" />""");
        }
        test.TakeSent();
        long before = session.Currency.Meso;
        Run(context, """<action name="arcade_three_two_one3_end_game" type="EndGame" />""");
        Assert.That(session.Currency.Meso - before, Is.EqualTo(meso));
        Assert.That(session.Field!.ThreeTwoOne!.RoundsCleared, Is.EqualTo(win ? 2 : 0));
        Assert.That(test.Errors, Is.Empty);
    }
    #endregion
}

// The arcade-record rows: one best per character, arcade and season, on an in-memory database.
public class ArcadeStorageTests {
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = 7, Name = name, Job = Job.Knight, Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        return db.CreateCharacter(model)!.Id;
    }

    [Test]
    public void HigherReplaces_EqualAndLowerKeep() {
        long id = CreateCharacter("alpha");
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveArcadeScore(id, 1, 40001, 500, 1_800_000_000), Is.True);
        Assert.That(db.SaveArcadeScore(id, 1, 40001, 500, 1_800_000_100), Is.False);
        Assert.That(db.SaveArcadeScore(id, 1, 40001, 400, 1_800_000_200), Is.False);
        Assert.That(db.GetArcadeBest(id, 1, 40001), Is.EqualTo(new ArcadeBest(1, 500, 1_800_000_000)));
        Assert.That(db.SaveArcadeScore(id, 1, 40001, 600, 1_800_000_300), Is.True);
        Assert.That(db.GetArcadeBest(id, 1, 40001), Is.EqualTo(new ArcadeBest(1, 600, 1_800_000_300)));
    }

    [Test]
    public void OneBestPerSeasonArcadeAndCharacter() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        using GameStorage.Request db = storage.Context();
        db.SaveArcadeScore(alpha, 1, 40001, 500, 1_800_000_000);
        db.SaveArcadeScore(alpha, 2, 40001, 30, 1_800_000_000);
        db.SaveArcadeScore(alpha, 1, 40002, 90, 1_800_000_000);
        db.SaveArcadeScore(beta, 1, 40001, 800, 1_800_000_000);

        Assert.That(db.GetArcadeBests(alpha, 40001).Select(best => (best.ArcadeId, best.Score)), Is.EqualTo(new[] { (1, 500), (2, 30) }));
        Assert.That(db.GetArcadeBests(alpha, 40002).Single().Score, Is.EqualTo(90));
        Assert.That(db.GetArcadeBest(beta, 1, 40001)!.Score, Is.EqualTo(800));
        Assert.That(db.GetArcadeBest(beta, 2, 40001), Is.Null);
    }

    // No character row, no record.
    [Test]
    public void UnknownCharacter_NotSaved() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveArcadeScore(12345, 1, 40001, 500, 1_800_000_000), Is.False);
    }
}
