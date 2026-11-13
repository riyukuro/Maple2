using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using DbAccount = Maple2.Database.Model.Account;

namespace Maple2.Server.Tests.Database;

// account.VipGoodsPurchases is a JSON column: NULL (rows from before the column, or no purchase) is an empty list, and
// an empty list is stored as NULL.
public class VipGoodsPurchasesJsonTests {
    [Test]
    public void RoundTrip() {
        var options = new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options;
        var account = new Account { Username = "vip" };
        account.VipGoodsPurchases.Add(new VipGoodsPurchase { GoodsId = 2001, Count = 2, LastPurchase = 1763000000 });
        account.VipGoodsPurchases.Add(new VipGoodsPurchase { GoodsId = 2003, Count = 1, LastPurchase = 1763000500 });
        long id;
        using (var context = new Ms2Context(options)) {
            DbAccount row = account;
            context.Account.Add(row);
            context.SaveChanges();
            id = row.Id;
        }

        using (var context = new Ms2Context(options)) {
            Account read = context.Account.AsNoTracking().Single(row => row.Id == id);
            Assert.That(read.VipGoodsPurchases.Select(entry => (entry.GoodsId, entry.Count, entry.LastPurchase)),
                Is.EqualTo(new[] { (2001, 2, 1763000000L), (2003, 1, 1763000500L) }));
        }
    }

    [Test]
    public void Empty_IsStoredAsNull() {
        DbAccount row = new Account { Username = "vip" };
        Assert.That(row.VipGoodsPurchases, Is.Null);
    }

    [Test]
    public void NullColumn_IsNoPurchase() {
        DbAccount row = new Account { Username = "vip" };
        Account read = row;
        Assert.That(read.VipGoodsPurchases, Is.Empty);
    }
}
