using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game;

// start_tutorial (52000065_qd tutorial.xml) has no client reader: the client's guide runs the tutorial and clears it
// with Tutorial 0x8A. The action must run without error and send nothing.
public class StartTutorialTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void StartTutorial_SendsNothing() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();

        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        GC.SuppressFinalize(owner);
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(owner, test.Session.Field);
        typeof(FieldEntity<TriggerModel>).GetField("<Value>k__BackingField", Any)!.SetValue(owner, new TriggerModel(1, "tutorial", Vector3.Zero, Vector3.Zero));
        var context = (TriggerContext) RuntimeHelpers.GetUninitializedObject(typeof(TriggerContext));
        typeof(TriggerContext).GetField("owner", Any)!.SetValue(context, owner);

        IAction action = TriggerFunctionMapping.ActionMap["start_tutorial"](null);
        Assert.That(action, Is.TypeOf<Trigger.StartTutorial>());
        Assert.DoesNotThrow(() => action.Execute(context));
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
