using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Serilog;

namespace Maple2.Server.Tests.Game;

// Runs one KR script action line on a field through the trigger loader's action map, on a bare trigger context (no
// trigger box or state machine), for tests whose field is not a TriggerTestBase field.
public static class ScriptAction {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Any) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    public static void Run(FieldManager field, string xml) {
        var trigger = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        Set<FieldEntity<TriggerModel>>(trigger, "<Field>k__BackingField", field);
        Set<FieldEntity<TriggerModel>>(trigger, "<Value>k__BackingField", new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero));
        var context = (TriggerContext) RuntimeHelpers.GetUninitializedObject(typeof(TriggerContext));
        Set<TriggerContext>(context, "owner", trigger);
        Set<TriggerContext>(context, "logger", new LoggerConfiguration().CreateLogger());
        Set<TriggerContext>(context, "lastDebugKey", "");
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement action = document.DocumentElement!;
        TriggerFunctionMapping.ActionMap[action.GetAttribute("name")](action.Attributes).Execute(context);
    }
}
