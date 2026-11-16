using System.Collections.Concurrent;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
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
}
