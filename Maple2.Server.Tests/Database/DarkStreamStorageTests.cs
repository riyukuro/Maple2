using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The Dark Stream best score (dark-stream-record) and the irrq.aspx answers built from it, on an in-memory
// database. The answers are read the way the client's GameRanking::DarkStream and MyRank read them.
public class DarkStreamStorageTests {
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name, Job job, long accountId = 7) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = accountId, Name = name, Job = job, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private bool Save(long characterId, int score, long time) {
        using GameStorage.Request db = storage.Context();
        return db.SaveDarkStreamScore(characterId, score, time);
    }

    private DarkStreamBest? Best(long characterId) {
        using GameStorage.Request db = storage.Context();
        return db.GetDarkStreamBest(characterId);
    }

    #region Store rule
    [Test]
    public void Replaces_FirstRun() {
        Assert.That(DarkStreamBest.Replaces(null, 0), Is.True);
        Assert.That(DarkStreamBest.Replaces(null, -50), Is.True);
    }

    [Test]
    public void Replaces_OnlyAHigherScore() {
        var best = new DarkStreamBest(4100, 1000);
        Assert.That(DarkStreamBest.Replaces(best, 4101), Is.True);
        Assert.That(DarkStreamBest.Replaces(best, 4100), Is.False);
        Assert.That(DarkStreamBest.Replaces(best, 3900), Is.False);
    }

    [Test]
    public void Store_FirstRunSetsScoreAndTime() {
        long id = CreateCharacter("alpha", Job.Knight);
        Assert.That(Best(id), Is.Null);

        Assert.That(Save(id, 4100, 1_800_000_000), Is.True);
        Assert.That(Best(id), Is.EqualTo(new DarkStreamBest(4100, 1_800_000_000)));
    }

    [Test]
    public void Store_BetterReplaces_EqualAndWorseKeepTheOldRecord() {
        long id = CreateCharacter("alpha", Job.Knight);
        Save(id, 4100, 1_800_000_000);

        Assert.That(Save(id, 4100, 1_800_000_500), Is.False);
        Assert.That(Best(id), Is.EqualTo(new DarkStreamBest(4100, 1_800_000_000)));

        Assert.That(Save(id, 3900, 1_800_001_000), Is.False);
        Assert.That(Best(id), Is.EqualTo(new DarkStreamBest(4100, 1_800_000_000)));

        Assert.That(Save(id, 5200, 1_800_002_000), Is.True);
        Assert.That(Best(id), Is.EqualTo(new DarkStreamBest(5200, 1_800_002_000)));
    }

    [Test]
    public void Store_UnknownCharacter_IsNotSaved() {
        Assert.That(Save(424242, 4100, 1_800_000_000), Is.False);
        Assert.That(Best(424242), Is.Null);
    }
    #endregion

    #region Web answers
    private static (int Type, int Count, ByteReader Reader) ReadBoardHeader(ByteWriter writer) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        int type = reader.ReadInt();
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        return (type, reader.ReadInt(), reader);
    }

    private ByteWriter Board(GameRankingType type, int jobCode, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return DarkStreamRanking.Board(db, type, jobCode, userName);
    }

    private ByteWriter Personal(GameRankingType type, int jobCode, long characterId) {
        using GameStorage.Request db = storage.Context();
        return DarkStreamRanking.Personal(db, type, jobCode, characterId);
    }

    private static int ReadPersonalRank(ByteWriter writer, GameRankingType type) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    [Test]
    public void Board_EmptyStore_HasNoRows() {
        CreateCharacter("alpha", Job.Knight);
        ByteWriter writer = Board(GameRankingType.DarkDescent, 0);
        (int type, int count, ByteReader reader) = ReadBoardHeader(writer);
        Assert.That(type, Is.EqualTo(0x21));
        Assert.That(count, Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Board_WithRows_BestFirst_TieByEarlierTime() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.KnightII);
        Save(alpha, 3000, 1_800_000_300);
        Save(beta, 5000, 1_800_000_100);
        Save(gamma, 3000, 1_800_000_200);

        (int _, int count, ByteReader reader) = ReadBoardHeader(Board(GameRankingType.DarkDescent, 0));
        Assert.That(count, Is.EqualTo(3));
        var rows = new List<(int Rank, long Id, string Name, int Score)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long id = reader.ReadLong();
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            int score = reader.ReadInt();
            Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
            rows.Add((rank, id, name, score));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        Assert.That(rows, Is.EqualTo(new[] {
            (1, beta, "beta", 5000),
            (2, gamma, "gamma", 3000),
            (3, alpha, "alpha", 3000),
        }));
    }

    [Test]
    public void Board_JobTab_KeepsThatJobCode() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.KnightII);
        Save(alpha, 3000, 1_800_000_300);
        Save(beta, 5000, 1_800_000_100);
        Save(gamma, 2000, 1_800_000_200);

        (int _, int count, ByteReader reader) = ReadBoardHeader(Board(GameRankingType.DarkDescent, (int) JobCode.Knight));
        Assert.That(count, Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(alpha));
    }

    [Test]
    public void Board_Search_GivesThatRowOnly() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 3000, 1_800_000_300);
        Save(beta, 5000, 1_800_000_100);

        (int _, int count, ByteReader reader) = ReadBoardHeader(Board(GameRankingType.DarkDescent, 0, "alpha"));
        Assert.That(count, Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(alpha));

        Assert.That(ReadBoardHeader(Board(GameRankingType.DarkDescent, 0, "nobody")).Count, Is.EqualTo(0));
    }

    [Test]
    public void Board_PreviousSeason_IsEmpty() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        Save(alpha, 3000, 1_800_000_300);
        (int type, int count, ByteReader _) = ReadBoardHeader(Board(GameRankingType.DarkDescentPreviousSeason, 0));
        Assert.That(type, Is.EqualTo(0x22));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void Board_DeletedCharacter_IsNotShown() {
        long alpha = CreateCharacter("alpha", Job.Knight, accountId: 9);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 3000, 1_800_000_300);
        Save(beta, 5000, 1_800_000_100);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(9, alpha), Is.True);
        }

        (int _, int count, ByteReader reader) = ReadBoardHeader(Board(GameRankingType.DarkDescent, 0));
        Assert.That(count, Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(beta));
        Assert.That(ReadPersonalRank(Personal(GameRankingType.DarkDescentPersonal, 0, alpha), GameRankingType.DarkDescentPersonal), Is.EqualTo(0));
    }

    [Test]
    public void Personal_EmptyStore_IsRankZero() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        Assert.That(ReadPersonalRank(Personal(GameRankingType.DarkDescentPersonal, 0, alpha), GameRankingType.DarkDescentPersonal), Is.EqualTo(0));
    }

    [Test]
    public void Personal_WithRows_IsTheBoardRank() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.Knight);
        Save(alpha, 3000, 1_800_000_300);
        Save(beta, 5000, 1_800_000_100);
        Save(gamma, 4000, 1_800_000_200);

        Assert.That(ReadPersonalRank(Personal(GameRankingType.DarkDescentPersonal, 0, alpha), GameRankingType.DarkDescentPersonal), Is.EqualTo(3));
        Assert.That(ReadPersonalRank(Personal(GameRankingType.DarkDescentPersonal, (int) JobCode.Knight, alpha), GameRankingType.DarkDescentPersonal), Is.EqualTo(2));
        Assert.That(ReadPersonalRank(Personal(GameRankingType.DarkDescentPersonalPreviousSeason, 0, alpha), GameRankingType.DarkDescentPersonalPreviousSeason), Is.EqualTo(0));
    }
    #endregion
}
