using System;
using System.Collections.Generic;
using System.Linq;
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
    #endregion

    #region Field
    private SpringFarmGame StartGame(int lives = 2) {
        var game = new SpringFarmGame(lives);
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
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ExpUp, SendOp.Meso, SendOp.PlayArcade }));
        ByteReader reader = Arcade(sent[2], 2);
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

        byte[] result = test.TakeSent().Last();
        ByteReader reader = Arcade(result, 2);
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(900));
        Assert.That(Best()!.Score, Is.EqualTo(900));
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
        Arcade(sent.Last(), 2);
        Assert.That(session.Field.SpringFarm!.Ended, Is.True);

        session.Field.SpringFarmDied();
        Assert.That(test.TakeSent(), Is.Empty);
    }
    #endregion

    #region Triggers
    private TriggerContext Context() {
        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        GC.SuppressFinalize(owner);
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(owner, session.Field);
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
        Assert.That(Arcade(sent.Last(), 2).ReadInt(), Is.EqualTo(470));
        Assert.That(game.Ended, Is.True);
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
