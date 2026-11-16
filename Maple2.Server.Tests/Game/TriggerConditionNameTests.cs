using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerHelpers = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game;

// A condition is named as the KR trigger script names it: the snake case of its class. The name is what the state
// list's condition lookup and the trigger logs print, so a class that drifts from its script name is not found.
public class TriggerConditionNameTests {
    private static object? Default(Type type) => type == typeof(string) ? "" : type.IsArray ? Array.CreateInstance(type.GetElementType()!, 0) :
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private static IEnumerable<Type> ConditionTypes() =>
        typeof(TriggerHelpers).GetNestedTypes().Where(type => typeof(ICondition).IsAssignableFrom(type) && !type.IsAbstract
            // The group conditions are the script's own keywords (any_one, all_of, true, always).
            && !type.Name.StartsWith("Group"));

    [Test]
    public void EveryConditionIsNamedAsItsClassInSnakeCase() {
        var wrong = new List<string>();
        int count = 0;
        foreach (Type type in ConditionTypes()) {
            count++;
            var condition = (ICondition) Activator.CreateInstance(type, type.GetConstructors()[0].GetParameters().Select(p => Default(p.ParameterType)).ToArray())!;
            string expected = Regex.Replace(type.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
            if (condition.Name != expected) {
                wrong.Add($"{type.Name}: {condition.Name}");
            }
            Assert.That(condition.Actions, Is.Empty);
            Assert.That(condition.NextState, Is.Null);
        }
        Assert.That(count, Is.GreaterThan(40));
        Assert.That(wrong, Is.Empty);
    }
}
