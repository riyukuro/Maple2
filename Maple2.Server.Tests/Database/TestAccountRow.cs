using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// The account MS2_TEST_ACCOUNT_ID names, made on a scratch MySQL game database that lacks it: the fixtures that store
// characters under that id met the character table's account foreign key on a fresh database (and passed only after
// another test had left such a row). The row is kept, as the fixtures' characters are.
public static class TestAccountRow {
    public static void Ensure(DbContextOptions options, long accountId) {
        using var context = new Ms2Context(options);
        if (context.Account.Any(account => account.Id == accountId)) {
            return;
        }
        Maple2.Database.Model.Account model = new Account { Username = "test" + accountId };
        model.Id = accountId;
        model.Password = "";
        context.Account.Add(model);
        try {
            context.SaveChanges();
        } catch (DbUpdateException) {
            // Made by another fixture in between.
            using var again = new Ms2Context(options);
            Assert.That(again.Account.Any(account => account.Id == accountId), Is.True);
        }
    }
}
