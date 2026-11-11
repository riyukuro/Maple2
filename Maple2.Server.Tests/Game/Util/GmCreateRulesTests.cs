using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;
using static Maple2.Server.Game.Util.GmCreateRules;

namespace Maple2.Server.Tests.Game.Util;

// The limits the GM create handlers apply (RequestItemCreate, RequestNpcCreate, RequestAdditionalEffect).
public class GmCreateRulesTests {
    [Test]
    public void Windows_NeedTheCommandFlagOnTopOfGm() {
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster, ItemPermission), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster, NpcPermission), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.SpawnItem, ItemPermission), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster | AdminPermissions.SpawnItem, ItemPermission), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster | AdminPermissions.SpawnItem, NpcPermission), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster | AdminPermissions.SpawnNpc, NpcPermission), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.Admin, ItemPermission), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.Admin, NpcPermission), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster, AdminPermissions.None), Is.True);
    }

    [TestCase(0)]
    [TestCase(7)]
    [TestCase(255)]
    public void Item_RankOutsideOneToSixIsRefused(int rarity) {
        Assert.That(PlanItem(1, rarity, false, 1, 10).Verdict, Is.EqualTo(ItemVerdict.Refuse));
        Assert.That(PlanItem(1, rarity, true, 1, 10).Verdict, Is.EqualTo(ItemVerdict.Refuse));
    }

    [Test]
    public void Item_RankOneAndSixPass() {
        Assert.That(PlanItem(1, 1, false, 1, 10), Is.EqualTo(new ItemPlan(ItemVerdict.Singles, 1)));
        Assert.That(PlanItem(1, 6, false, 1, 10), Is.EqualTo(new ItemPlan(ItemVerdict.Singles, 1)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Item_AmountBelowOneIsRefused(int amount) {
        Assert.That(PlanItem(amount, 1, false, 100, 10).Verdict, Is.EqualTo(ItemVerdict.Refuse));
        Assert.That(PlanItem(amount, 1, true, 1, 10).Verdict, Is.EqualTo(ItemVerdict.Refuse));
    }

    [Test]
    public void Item_CurrencyIsCapped() {
        Assert.That(PlanItem(5000, 1, true, 1, 0), Is.EqualTo(new ItemPlan(ItemVerdict.Currency, 5000)));
        Assert.That(PlanItem(int.MaxValue, 1, true, 1, 0), Is.EqualTo(new ItemPlan(ItemVerdict.Currency, MaxCurrencyAmount)));
    }

    [Test]
    public void Item_StackableGivesOneStackAtMost() {
        Assert.That(PlanItem(30, 1, false, 100, 5), Is.EqualTo(new ItemPlan(ItemVerdict.Stack, 30)));
        Assert.That(PlanItem(int.MaxValue, 1, false, 100, 5), Is.EqualTo(new ItemPlan(ItemVerdict.Stack, 100)));
    }

    [Test]
    public void Item_SinglesAreCappedByFreeSlots() {
        Assert.That(PlanItem(3, 4, false, 1, 5), Is.EqualTo(new ItemPlan(ItemVerdict.Singles, 3)));
        Assert.That(PlanItem(int.MaxValue, 4, false, 1, 5), Is.EqualTo(new ItemPlan(ItemVerdict.Singles, 5)));
        Assert.That(PlanItem(3, 4, false, 1, 0).Verdict, Is.EqualTo(ItemVerdict.Full));
    }

    [Test]
    public void Npc_RequestIsClampedToOneToFifty() {
        Assert.That(PlanNpc(0, 0), Is.EqualTo(1));
        Assert.That(PlanNpc(-5, 0), Is.EqualTo(1));
        Assert.That(PlanNpc(7, 0), Is.EqualTo(7));
        Assert.That(PlanNpc(int.MaxValue, 0), Is.EqualTo(MaxNpcPerRequest));
    }

    [Test]
    public void Npc_FieldTotalIsCapped() {
        Assert.That(PlanNpc(50, MaxNpcPerField - 50), Is.EqualTo(50));
        Assert.That(PlanNpc(50, MaxNpcPerField - 3), Is.EqualTo(3));
        Assert.That(PlanNpc(1, MaxNpcPerField), Is.EqualTo(0));
        Assert.That(PlanNpc(1, MaxNpcPerField + 10), Is.EqualTo(0));
    }

    [Test]
    public void Effect_OnlyNewObjectsCountAsCreated() {
        object old = new();
        object fresh = new();

        // Already on before the request (refreshed): nothing to record.
        Assert.That(GmEffectRules.Created(new List<object> { old }, new List<object> { old }), Is.Empty);
        // The add was refused: nothing to record.
        Assert.That(GmEffectRules.Created(new List<object>(), new List<object>()), Is.Empty);
        // A new buff next to an old one.
        Assert.That(GmEffectRules.Created(new List<object> { old }, new List<object> { old, fresh }), Is.EqualTo(new[] { fresh }));
    }

    [Test]
    public void Effect_ResetLooksForTheSameObject() {
        string recorded = new string('a', 3);
        string later = new string('a', 3); // equal, another object: a buff that came back from a skill

        Assert.That(GmEffectRules.Contains(new List<string> { recorded }, recorded), Is.True);
        Assert.That(GmEffectRules.Contains(new List<string> { later }, recorded), Is.False);
    }
}
