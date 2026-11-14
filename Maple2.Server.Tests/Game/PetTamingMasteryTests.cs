using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// Pet taming mastery: a capture's amount by rarity (the server's own), the PetMastery event's capture and other rates
// (the client's ApplyPetMasteryBonus rule), and the mastery packet's sub type that picks the client's text (3200 a
// capture). The capture itself is in QuestConditionWiringTests (PetCatch_GivesPetTamingMastery). The event ids are
// made up.
public class PetTamingMasteryTests {
    private const int SnareRecipe = 20000001;
    private const int SnareId = 63100000;
    private TestGameSession test = null!;

    [TestCase(1, 30)]
    [TestCase(2, 60)]
    [TestCase(3, 120)]
    [TestCase(4, 240)]
    [TestCase(0, 30)]
    [TestCase(5, 240)]
    public void CaptureMastery_ByRarity(int rarity, int mastery) {
        Assert.That(PetRules.CaptureMastery(rarity), Is.EqualTo(mastery));
    }

    // gain + gain * rate / 10000 as a float, truncated; nothing added without a rate or a gain.
    [TestCase(30, 10000, 60)]
    [TestCase(30, 5000, 45)]
    [TestCase(30, 3333, 39)]
    [TestCase(240, 2500, 300)]
    [TestCase(30, 0, 30)]
    [TestCase(0, 10000, 0)]
    [TestCase(int.MaxValue, 10000, int.MaxValue)]
    public void PetMasteryWithEvent_IsTheClientRule(int gain, int rate, int expected) {
        Assert.That(EventSaleRules.PetMasteryWithEvent(gain, rate), Is.EqualTo(expected));
    }

    private static MasteryRecipeTable.Entry Snare() =>
        new(SnareRecipe, MasteryType.PetTaming, NoRewardExp: false, RequiredMastery: 0, RequiredMeso: 0, RequiredQuests: [],
            RequiredLevel: 0, RewardExp: 0, RewardMastery: 30, HighRateLimitCount: 5, NormalRateLimitCount: 10,
            RequiredItems: [], HabitatMapId: [], RewardItems: [new ItemComponent(SnareId, 1, 1, ItemTag.None)]);

    private GameSession Start(params (int Id, PetMastery Rates, bool Over)[] events) {
        test = new TestGameSession();
        foreach ((int id, PetMastery rates, bool over) in events) {
            test.AddEvent(id, GameEventType.PetMastery, rates, over ? DateTime.Now.AddDays(-9) : null, over ? DateTime.Now.AddDays(-2) : null);
        }
        test.Register(TestGameSession.Metadata(SnareId));
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry> { [SnareRecipe] = Snare() }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.manufacturing] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return session;
    }

    // The pet taming mastery packets sent: (total, sub type).
    private List<(int Total, int SubType)> MasterySent() {
        return test.TakeSent()
            .Where(packet => TestGameSession.Op(packet) == SendOp.Mastery && packet[2] == 0 && packet[3] == (byte) MasteryType.PetTaming)
            .Select(packet => (BitConverter.ToInt32(packet, 4), BitConverter.ToInt32(packet, 8)))
            .ToList();
    }

    [Test]
    public void Rates_HighestOfEachKind() {
        GameSession session = Start((731001, new PetMastery(2500, 0), false), (731002, new PetMastery(5000, 1000), false),
            (731003, new PetMastery(0, 3000), false));

        Assert.That(EventSaleRules.PetMasteryRates(session.FindEvent(GameEventType.PetMastery)), Is.EqualTo((5000, 3000)));
    }

    // A snare craft gives its 30 with the other rate (a capture rate adds nothing to it), named as a craft (3107).
    [TestCase(0, 0, false, 30)]
    [TestCase(10000, 5000, false, 45)]
    [TestCase(10000, 0, false, 30)]
    [TestCase(0, 5000, true, 30)]
    public void SnareCraft_OtherRate(int captureRate, int otherRate, bool over, int mastery) {
        GameSession session = captureRate > 0 || otherRate > 0
            ? Start((731001, new PetMastery(captureRate, otherRate), over))
            : Start();
        var handler = new MasteryHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(SnareRecipe);
        }));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Mastery[MasteryType.PetTaming], Is.EqualTo(mastery));
        Assert.That(MasterySent(), Is.EqualTo(new List<(int, int)> { (mastery, (int) ExpMessageCode.manufacturing) }));
    }

    // A gain names its reason in the packet; the next plain change names none.
    [Test]
    public void Add_SendsTheSubType() {
        GameSession session = Start();
        session.Mastery.Add(MasteryType.PetTaming, 60, (int) ExpMessageCode.petTaming);
        session.Mastery[MasteryType.PetTaming] += 10;

        Assert.That(MasterySent(), Is.EqualTo(new List<(int, int)> { (60, 3200), (70, 0) }));
    }

    // A capped mastery or no gain sends nothing (the client would show a gain of 0).
    [Test]
    public void Add_CappedOrNothingSendsNothing() {
        GameSession session = Start();
        session.Mastery.Add(MasteryType.PetTaming, 0, (int) ExpMessageCode.petTaming);
        session.Mastery[MasteryType.PetTaming] = Constant.PetTamingMasteryMax;
        test.TakeSent();
        session.Mastery.Add(MasteryType.PetTaming, 240, (int) ExpMessageCode.petTaming);

        Assert.That(MasterySent(), Is.Empty);
        Assert.That(session.Mastery[MasteryType.PetTaming], Is.EqualTo(Constant.PetTamingMasteryMax));
    }
}
