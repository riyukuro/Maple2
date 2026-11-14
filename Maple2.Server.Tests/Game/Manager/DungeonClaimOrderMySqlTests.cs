using System;
using System.Collections.Generic;
using Maple2.Database.Extensions;
using Maple2.Model.Game.Dungeon;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// The claim order on MySQL, where a store is one transaction (the in-memory store ignores them): every test of
// DungeonClaimOrderTests again, and a store refused after its mark is written (at a mail or the mail's item), which
// must leave neither the mark nor any mail stored. Runs only when MS2_TEST_GAME_DB holds the connection string of a
// scratch MySQL game database (GuidFormat=Binary16).
public class DungeonClaimOrderMySqlTests : DungeonClaimOrderTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    [SetUp]
    public void RequireDatabase() {
        if (Connection == null) {
            Assert.Ignore("the stores' rollback needs a real database: set MS2_TEST_GAME_DB");
        }
    }

    protected override TestGameSession NewSession() =>
        new(new DbContextOptionsBuilder().UseMySql(Connection!, ServerVersion.AutoDetect(Connection!)).Options);

    // The second rank mail is refused after the ranks given and the first mail are written: none of them is stored,
    // the ranks are back in memory, and the next clear mails both ranks once.
    [Test]
    public void ARankStoreRefusedAtItsSecondMail_StoresNeitherTheRanksNorAMail() {
        StartInRoom();
        long now = DateTime.Now.ToEpochSeconds();
        int mailSaves = 0;
        Func<DbContext, bool> mail = Fails("Mail");
        test.FailSave = context => mail(context) && ++mailSaves == 2;
        Clear(250, now); // B: C and B
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(mailSaves, Is.EqualTo(2), "the first mail was written before the refusal");
            Assert.That(Mails(), Is.Empty);
            Assert.That(StoredRankClaimed(), Is.EqualTo(0));
            Assert.That(session.Player.Value.Unlock.DungeonRankRewards.ContainsKey(RewardId), Is.False);
        });

        Clear(250, now);
        Assert.That(Mails(), Is.EqualTo(new[] { FirstMail.ToString(), (FirstMail + 1).ToString() }));
        Assert.That(StoredRankClaimed(), Is.EqualTo(2));
    }

    // The season mail's item is refused after the week's scores (0) and the mail are written: the scores stay 500 in
    // the database and in memory, no mail, and the next run mails the week once.
    [Test]
    public void ASeasonStoreRefusedAtTheMailsItem_KeepsTheScores() {
        long now = DateTime.Now.ToEpochSeconds();
        DungeonRecord record = StartInSeasonRoom(now);

        test.FailSave = Fails("Item");
        session.Dungeon.MailSeasonRewards();
        test.FailSave = null;
        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That(record.CurrentRecord, Is.EqualTo(500));
            Assert.That(StoredRecord(SeasonRoom)?.Score, Is.EqualTo((short) 500));
        });

        session.Dungeon.MailSeasonRewards();
        Assert.That(Mails(), Is.EqualTo(new[] { SeasonMail.ToString() }));
        Assert.That(StoredRecord(SeasonRoom)?.Score, Is.EqualTo((short) 0));
    }
}
