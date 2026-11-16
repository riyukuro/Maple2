using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The quest condition of a divorce, from a real KR row: the divorce a filed one becomes when its cooling off ends
// (MarriageManager.CheckDivorceExpiration). Its writes are bulk updates the in-memory store cannot run: MySQL only
// (MS2_TEST_GAME_DB).
public class WeddingDivorceConditionTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    // 23100447: <condition type="wedding_divorce" value="1" />: the read of the marriage divorced counts once; the
    // next check, with the marriage still divorced, does not count again.
    [Test]
    public void Divorce_23100447_TheEndOfTheCoolingOff() {
        if (Connection == null) {
            Assert.Ignore("the divorce's writes are bulk updates: set MS2_TEST_GAME_DB");
        }
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        var test = new TestGameSession(options, Gender.Male);
        test.Start();
        long marriageId;
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = $"partner{test.AccountId}" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = $"partner{test.AccountId}", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            marriageId = db.CreateMarriage(test.CharacterId, partnerId)!.Id;
            DateTime filed = DateTime.UtcNow.AddDays(-8);
            Assume.That(db.SetMarried(marriageId, filed.AddDays(-1)), Is.True);
            Assume.That(db.FileDivorce(marriageId, test.CharacterId, filed), Is.True);
            test.Session.Marriage.Marriage = db.GetMarriage(test.CharacterId)!;
        }
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.wedding_divorce, 1, null, null)],
        });
        Assume.That(test.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Divorced));

        test.Session.Marriage.CheckDivorceExpiration();
        Assert.That(test.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Divorced));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        test.Session.Marriage.CheckDivorceExpiration();
        test.Session.Marriage.Reload();

        Assert.That(test.Session.Marriage.Marriage.Id, Is.EqualTo(marriageId));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
}
