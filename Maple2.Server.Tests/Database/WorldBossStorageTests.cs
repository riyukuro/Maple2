using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The world boss board (the ranking dialog's board 0xb) built from the kept clears, on an in-memory database. The
// answers are read the way the client's GameRanking::BossDamageCount (0x70), BossDamageDate (0x79),
// BossDamageAmount (0x84) and MyRank (0x6f, 0x83) read them. Made-up ids.
public class WorldBossStorageTests {
    private const int Board = 70011;

    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name, Job job) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = 7, Name = name, Job = job, Level = 60, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private long Clear(long leaderId, long time, long damage, params long[] members) {
        using GameStorage.Request db = storage.Context();
        return db.AddBossClear(Board, 23900004, leaderId, 900, time, members, damage);
    }

    private ByteWriter Answer(GameRankingType type, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return BossRanking.WorldBossBoard(db, type, Board, userName);
    }

    private int Personal(GameRankingType type, long characterId) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = BossRanking.WorldBossPersonal(db, type, Board, characterId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    private static ByteReader Header(ByteWriter writer, GameRankingType type, out int count) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        count = reader.ReadInt();
        return reader;
    }

    private static List<long> Members(ByteReader reader) {
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int count = reader.ReadInt();
        var ids = new List<long>();
        for (int i = 0; i < count; i++) {
            ids.Add(reader.ReadLong());
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(60));
            reader.ReadInt();
        }
        return ids;
    }

    // BossDamageDate::Read: int rank, long, long leader, int, long point, date, members.
    private static List<(int Rank, long RowId, long Leader, long Point, List<long> Members)> ReadFirstClears(ByteWriter writer) {
        ByteReader reader = Header(writer, GameRankingType.WorldBossFirstClears, out int count);
        var rows = new List<(int, long, long, long, List<long>)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long rowId = reader.ReadLong();
            long leader = reader.ReadLong();
            Assert.That(reader.ReadInt(), Is.EqualTo(Board));
            long point = reader.ReadLong();
            rows.Add((rank, rowId, leader, point, Members(reader)));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    // BossDamageAmount::Read: int rank, long, long leader, long, int, long, long, long point (m_50), long, int
    // rank point, date, members.
    private static List<(int Rank, long Leader, long Point, List<long> Members)> ReadDamage(ByteWriter writer) {
        ByteReader reader = Header(writer, GameRankingType.WorldBossDamage, out int count);
        var rows = new List<(int, long, long, List<long>)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            reader.ReadLong();
            long leader = reader.ReadLong();
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(Board));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            long point = reader.ReadLong();
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            rows.Add((rank, leader, point, Members(reader)));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    [Test]
    public void FirstClears_EarliestFirst_WithThePartyDamage() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Priest);
        long late = Clear(alpha, 1_800_000_500, 90_000, alpha);
        long early = Clear(beta, 1_800_000_100, 40_000, beta, alpha);

        var rows = ReadFirstClears(Answer(GameRankingType.WorldBossFirstClears));

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0], Is.EqualTo((1, early, beta, 40_000L, rows[0].Members)));
        Assert.That(rows[0].Members, Is.EquivalentTo(new[] { alpha, beta }));
        Assert.That(rows[1].Rank, Is.EqualTo(2));
        Assert.That(rows[1].RowId, Is.EqualTo(late));
        Assert.That(rows[1].Point, Is.EqualTo(90_000));
    }

    // Most damage first; equal damage goes by the earlier clear.
    [Test]
    public void Damage_MostFirst_ThenTheEarlierClear() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Priest);
        long gamma = CreateCharacter("gamma", Job.Archer);
        Clear(alpha, 1_800_000_100, 50_000, alpha);
        Clear(beta, 1_800_000_300, 80_000, beta);
        Clear(gamma, 1_800_000_200, 80_000, gamma, alpha);

        var rows = ReadDamage(Answer(GameRankingType.WorldBossDamage));

        Assert.That(rows.ConvertAll(row => (row.Rank, row.Leader, row.Point)), Is.EqualTo(new[] {
            (1, gamma, 80_000L), (2, beta, 80_000L), (3, alpha, 50_000L),
        }));
        // The own damage rank is the character's best clear's place; none without a clear.
        Assert.That(Personal(GameRankingType.WorldBossDamagePersonal, alpha), Is.EqualTo(1));
        Assert.That(Personal(GameRankingType.WorldBossDamagePersonal, beta), Is.EqualTo(2));
        Assert.That(Personal(GameRankingType.WorldBossDamagePersonal, 424242), Is.EqualTo(0));
    }

    // 0x70 is the clear count rows of BossClearCount, with its own type; 0x6f the rank on it; a search name gives
    // that character's row.
    [Test]
    public void ClearCount_TheCountRows_WithTheWorldBossType() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Priest);
        Clear(alpha, 1_800_000_100, 10, alpha, beta);
        Clear(beta, 1_800_000_200, 10, beta);

        ByteReader reader = Header(Answer(GameRankingType.WorldBossClearCount), GameRankingType.WorldBossClearCount, out int count);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(beta));

        Header(Answer(GameRankingType.WorldBossClearCount, "alpha"), GameRankingType.WorldBossClearCount, out int searched);
        Assert.That(searched, Is.EqualTo(1));
        Assert.That(Personal(GameRankingType.WorldBossClearCountPersonal, alpha), Is.EqualTo(2));
        Assert.That(Personal(GameRankingType.WorldBossClearCountPersonal, beta), Is.EqualTo(1));
    }

    // A clear without a damage (the other boss rooms) keeps 0.
    [Test]
    public void Damage_DefaultsToZero() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        using (GameStorage.Request db = storage.Context()) {
            db.AddBossClear(Board, 23900004, alpha, 900, 1_800_000_100, [alpha]);
        }

        var rows = ReadDamage(Answer(GameRankingType.WorldBossDamage));
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Point, Is.EqualTo(0));
    }
}
