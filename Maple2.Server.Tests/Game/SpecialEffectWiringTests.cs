using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Validators;

namespace Maple2.Server.Tests.Game;

// The KR special effects where the server applies them (SpecialEffectRules): each held as an enabled buff, rolling
// with a fixed random (0 wins every roll, 0.999 loses each one under 1).
public class SpecialEffectWiringTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int nextId = 90100001;

    internal static void Hold(FieldPlayer player, SpecialEffectMetadata effect, double roll = 0) {
        int id = nextId++;
        AdditionalEffectMetadata metadata = DisabledBuffTests.Effect(id, property: DisabledBuffTests.Property with { SpecialEffect = effect });
        Buff buff = DisabledBuffTests.Held(metadata, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, player);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, player);
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        buffs[id] = [buff];
        player.Buffs.SpecialEffectRandom = new SpecialEffectRulesTests.FixedRandom(roll);
    }

    private TestGameSession test = null!;

    private GameSession Login() {
        test = new TestGameSession();
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    // 110001022: a daily quest's meso, 4 times more (5 in all) on a 5 % roll, with its message; not for another
    // repeat type or a lost roll.
    private long QuestMeso(int repeat, SpecialEffectMetadata? effect, double roll) {
        GameSession session = Login();
        if (effect != null) {
            Hold(session.Player, effect, roll);
        }
        QuestMetadata data = TestGameSession.QuestData(91000001, QuestType.DailyMission);
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Repeat = repeat },
            CompleteReward = data.CompleteReward with { Meso = 1000 },
        });
        long before = session.Currency.Meso;
        Assert.That(session.Quest.Complete(quest), Is.True);
        return session.Currency.Meso - before;
    }

    [Test]
    public void Quest_DailyMesoRaised() {
        long plain = QuestMeso(2, null, 0);
        Assert.That(plain, Is.GreaterThan(0));
        Assert.That(QuestMeso(2, SpecialEffectRulesTests.QuestMeso, 0), Is.EqualTo(plain * 5));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.Notice), "activateMsg");
        Assert.That(QuestMeso(2, SpecialEffectRulesTests.QuestMeso, 0.999), Is.EqualTo(plain), "lost roll");
        Assert.That(QuestMeso(1, SpecialEffectRulesTests.QuestMeso, 0), Is.EqualTo(QuestMeso(1, null, 0)), "not daily");
        Assert.That(QuestMeso(2, SpecialEffectRulesTests.QuestExp, 0), Is.EqualTo(plain), "the exp effect");
    }

    // 110001020: a wooden chest (normalChest) gives individual drop box 382000002 more; a golden one does not.
    [TestCase(ExpType.normalChest, 1)]
    [TestCase(ExpType.rareChest, 0)]
    public void Chest_GivesTheDropBox(ExpType chest, int given) {
        const int prize = 20000001;
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(prize));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [382000002] = TestGameSession.DropBox([(prize, 1)]),
        }));
        GameSession session = test.Start();
        Hold(session.Player, SpecialEffectRulesTests.WoodChest);

        InteractObjectHandler.GiveChestDropBoxes(session, chest);

        Assert.That(session.Item.Inventory.Find(prize).Sum(item => item.Amount), Is.EqualTo(given));
    }

    // 110001013: 6 % less damage from an elite (class 3 or 7), none less from another npc or a player.
    [Test]
    public void NpcRank_EliteHitsLess() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Assert.That(DamageCalculator.NpcRankDamageFactor(NpcRankDamageTests.Npc(3), player), Is.EqualTo(1f), "not held");
        Hold(player, SpecialEffectRulesTests.EliteRank);
        Assert.That(DamageCalculator.NpcRankDamageFactor(NpcRankDamageTests.Npc(3), player), Is.EqualTo(0.94f));
        Assert.That(DamageCalculator.NpcRankDamageFactor(NpcRankDamageTests.Npc(7), player), Is.EqualTo(0.94f));
        Assert.That(DamageCalculator.NpcRankDamageFactor(NpcRankDamageTests.Npc(1), player), Is.EqualTo(1f));
        Assert.That(DamageCalculator.NpcRankDamageFactor(player, player), Is.EqualTo(1f));
    }

    // A buff whose begin condition fails holds no special effect.
    [Test]
    public void DisabledBuff_HoldsNone() {
        Buff off = DisabledBuffTests.Held(DisabledBuffTests.Effect(90100000, property: DisabledBuffTests.Property with { SpecialEffect = SpecialEffectRulesTests.Fishing4 }), false);
        Assert.That(BuffManager.SpecialEffects([off]), Is.Empty);
    }
}
