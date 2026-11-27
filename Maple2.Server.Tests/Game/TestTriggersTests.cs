using System;
using System.IO;
using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The GM test triggers (trigger load <name>): the name check, the failures that start nothing, and the shipped scripts.
public class TestTriggersTests {
    private string dir = null!;

    [SetUp]
    public void SetUp() {
        dir = Path.Combine(Path.GetTempPath(), "testtriggers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(dir, true);
    }

    [TestCase("gravity")]
    [TestCase("a_1")]
    public void ValidName(string name) {
        Assert.That(TestTriggers.IsValidName(name), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("../gravity")]
    [TestCase("..")]
    [TestCase("a/b")]
    [TestCase("a\\b")]
    [TestCase("a.b")]
    [TestCase("Gravity")]
    [TestCase("a b")]
    public void InvalidName_IsRejectedBeforeAnyRead(string? name) {
        Assert.That(TestTriggers.IsValidName(name), Is.False);
        System.IO.File.WriteAllText(Path.Combine(dir, "x.xml"), "<ms2><state name=\"a\" /></ms2>");
        Assert.That(TestTriggers.Load(Path.Combine(dir, "sub"), name!, "02000001_bf", out string error), Is.Null);
        Assert.That(error, Does.StartWith("Invalid"));
    }

    [Test]
    public void MissingFile_StartsNothing() {
        Assert.That(TestTriggers.Load(dir, "nothing", "02000001_bf", out string error), Is.Null);
        Assert.That(error, Does.StartWith("No test trigger"));
    }

    [Test]
    public void BrokenXml_StartsNothing() {
        System.IO.File.WriteAllText(Path.Combine(dir, "broken.xml"), "<ms2><state name=\"a\">");
        Assert.That(TestTriggers.Load(dir, "broken", "02000001_bf", out string error), Is.Null);
        Assert.That(error, Does.Contain("does not parse"));
    }

    [Test]
    public void WrongRoot_StartsNothing() {
        System.IO.File.WriteAllText(Path.Combine(dir, "root.xml"), "<trigger><state name=\"a\" /></trigger>");
        Assert.That(TestTriggers.Load(dir, "root", "02000001_bf", out string error), Is.Null);
        Assert.That(error, Does.Contain("does not parse"));
    }

    [Test]
    public void NoStates_StartsNothing() {
        System.IO.File.WriteAllText(Path.Combine(dir, "empty.xml"), "<ms2></ms2>");
        Assert.That(TestTriggers.Load(dir, "empty", "02000001_bf", out string error), Is.Null);
        Assert.That(error, Does.Contain("no states"));
    }

    // gravity.xml: six states, each sets gravity once, and the wait_tick chain walks them in order to the last one.
    [Test]
    public void Gravity_ParsesEveryElement_AndWalksTheValues() {
        string path = Path.Combine(TestTriggers.Dir, "gravity.xml");
        Assert.That(System.IO.File.Exists(path), Is.True, path);
        Trigger? trigger = TestTriggers.Load(TestTriggers.Dir, "gravity", "02000001_bf", out string error);
        Assert.That(trigger, Is.Not.Null, error);

        var document = new XmlDocument();
        document.Load(path);
        Assert.That(trigger!.States.Sum(state => state.Enter?.Actions.Count ?? 0), Is.EqualTo(document.SelectNodes("//action")!.Count));
        Assert.That(trigger.States.Sum(state => state.Conditions.Count), Is.EqualTo(document.SelectNodes("//condition")!.Count));

        Assert.That(trigger.States.Select(state => state.Name), Is.EqualTo(new[] {
            "gravity_37", "gravity_9_8", "gravity_up_55", "gravity_0", "gravity_32", "gravity_end",
        }));
        foreach (Trigger.State state in trigger.States) {
            Assert.That(state.Enter!.Actions.Single(), Is.InstanceOf<Trigger.SetGravity>());
        }
        for (int i = 0; i + 1 < trigger.States.Count; i++) {
            ICondition wait = trigger.States[i].Conditions.Single();
            Assert.That(wait, Is.InstanceOf<Trigger.WaitTick>());
            Assert.That(wait.NextState, Is.EqualTo(trigger.States[i + 1].Name));
        }
        Assert.That(trigger.States[^1].Conditions, Is.Empty);
    }
}
