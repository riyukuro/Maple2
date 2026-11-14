using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using Maple2.Server.Game.Trigger.Helpers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The trigger scripts write decimals with a period and negatives with '-'. The servers set en-US on their main thread,
// but triggers run on the field threads, which take the default culture: under de-DE "0.5" is 5 (the period groups
// digits) and "1,5" is 1.5. The parses must not depend on it.
public class TriggerCultureTests {
    private static T OnThread<T>(string culture, Func<T> body) {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => {
            try {
                result = body();
            } catch (Exception ex) {
                error = ex;
            }
        });
        thread.CurrentCulture = new CultureInfo(culture);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }

    [TestCase("de-DE")]
    [TestCase("fr-FR")]
    [TestCase("sv-SE")]
    public void Floats(string culture) {
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseFloat("0.5")), Is.EqualTo(0.5f));
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseFloat("-1.25")), Is.EqualTo(-1.25f));
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseFloat("1,5")), Is.EqualTo(15f)); // as on en-US: a comma groups digits
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseVector3("1.5,-2.25,300")),
            Is.EqualTo(new Vector3(1.5f, -2.25f, 300f)));
    }

    [TestCase("de-DE")]
    [TestCase("sv-SE")]
    public void Ints(string culture) {
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseInt("-30")), Is.EqualTo(-30));
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseIntArray("1,2,3")), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(OnThread(culture, () => TriggerFunctionMapping.ParseIntArray("5000-5002")), Is.EqualTo(new[] { 5000, 5001, 5002 }));
    }

    // The control: without the invariant parse, the same text reads differently on these threads.
    [Test]
    public void CultureTrapExists() {
        Assert.That(OnThread("de-DE", () => float.TryParse("0.5", out float v) ? v : 0f), Is.Not.EqualTo(0.5f));
        Assert.That(OnThread("de-DE", () => float.TryParse("1,5", out float v) ? v : 0f), Is.EqualTo(1.5f));
    }
}
