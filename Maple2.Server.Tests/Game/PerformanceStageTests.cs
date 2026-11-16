using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// The music stage of the performance map (PerformanceStageManager): one performer at a time, only the owner ends
// it, the field's MusicConcert property stands while it runs, and the stage clears when its owner leaves the
// performance map. The ids are made up.
public class PerformanceStageTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private TestGameSession second = null!;
    private PerformanceStageManager stage = null!;
    private ConcurrentDictionary<FieldProperty, IFieldProperty> properties = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        second = new TestGameSession();
        second.Start();
        typeof(Maple2.Server.Game.Session.GameSession).GetProperty(nameof(Maple2.Server.Game.Session.GameSession.CharacterId))!.SetValue(second.Session, second.Session.CharacterId + 1);
        properties = new ConcurrentDictionary<FieldProperty, IFieldProperty>();
        typeof(FieldManager).GetField("fieldProperties", Any)!.SetValue(test.Session.Field, properties);
        stage = new PerformanceStageManager(test.Session.Field!);
    }

    // IsCurrentPerformer reads the owner's party, which the test session does not have.
    private object? Owner => typeof(PerformanceStageManager).GetField("performanceOwner", Any)!.GetValue(stage);

    [Test]
    public void StartingAddsTheConcertProperty_AndASecondStartIsRefused() {
        stage.StartPerformance(test.Session);
        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.True);
        Assert.That(Owner, Is.SameAs(test.Session));

        stage.StartPerformance(second.Session);
        Assert.That(Owner, Is.SameAs(test.Session));
    }

    [Test]
    public void OnlyTheOwnerEnds_AndTheConcertPropertyGoes() {
        stage.StartPerformance(test.Session);

        stage.EndPerformance(second.Session);
        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.True);

        stage.EndPerformance(test.Session);
        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.False);
        Assert.That(Owner, Is.Null);
    }

    [Test]
    public void EndingWithNoPerformance_ChangesNothing() {
        stage.EndPerformance(test.Session);
        Assert.That(properties, Is.Empty);
    }

    // The owner stands in a map that is not the performance map: the next update ends the stage.
    [Test]
    public void UpdateEndsTheStageOnceTheOwnerLeftThePerformanceMap() {
        stage.StartPerformance(test.Session);
        Assert.That(test.Session.Field!.MapId, Is.Not.EqualTo(Constant.PerformanceMapId));

        stage.Update();

        Assert.That(Owner, Is.Null);
        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.False);
    }

    [Test]
    public void UpdateWithoutAPerformance_DoesNothing() {
        Assert.DoesNotThrow(() => stage.Update());
    }

    private static void PartyOf(TestGameSession session, int partyId) {
        var manager = (PartyManager) RuntimeHelpers.GetUninitializedObject(typeof(PartyManager));
        if (partyId != 0) {
            typeof(PartyManager).GetProperty(nameof(PartyManager.Party))!.SetValue(manager, new Maple2.Model.Game.Party.Party(partyId, 1, 1, "leader"));
        }
        session.Session.Party = manager;
    }

    // A solo performer: only the owner performs (a trigger's MoveUser does not move it off the stage).
    [Test]
    public void ASoloPerformance_HasOnlyItsOwnerPerforming() {
        PartyOf(test, 0);
        PartyOf(second, 0);
        Assert.That(stage.IsCurrentPerformer(test.Session), Is.False, "no performance yet");

        stage.StartPerformance(test.Session);

        Assert.That(stage.IsCurrentPerformer(test.Session), Is.True);
        Assert.That(stage.IsCurrentPerformer(second.Session), Is.False);
    }

    // An owner in a party performs with the party's members.
    [TestCase(5, true)]
    [TestCase(6, false)]
    [TestCase(0, false)]
    public void APartyPerformance_HasThePartysMembersPerforming(int secondParty, bool performing) {
        PartyOf(test, 5);
        PartyOf(second, secondParty);

        stage.StartPerformance(test.Session);

        Assert.That(stage.IsCurrentPerformer(second.Session), Is.EqualTo(performing));
    }

    // A performance runs MaxPerformanceDuration at most: the first update after its end tick clears the stage.
    [Test]
    public void UpdateEndsAPerformancePastItsTime() {
        stage.StartPerformance(test.Session);
        typeof(PerformanceStageManager).GetField("performanceEndTick", Any)!.SetValue(stage, test.Session.Field!.FieldTickInt - 1);

        stage.Update();

        Assert.That(Owner, Is.Null);
        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.False);
    }
}
