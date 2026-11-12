using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class InteractRulesTests {
    [Test]
    public void NamesBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(2);
        writer.WriteString("a1");
        writer.WriteString("b2");
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(InteractRules.ReadNames(reader), Is.EqualTo(new[] { "a1", "b2" }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Reach() {
        var player = new Vector3(0, 0, 0);
        Assert.That(InteractRules.InReach(player, new Vector3(300, 0, 0), 300), Is.True);
        Assert.That(InteractRules.InReach(player, new Vector3(300, 0, 0) + new Vector3(InteractRules.ReachSlack, 0, 0), 300), Is.True);
        Assert.That(InteractRules.InReach(player, new Vector3(451, 0, 0), 300), Is.False);
        Assert.That(InteractRules.InReach(player, new Vector3(0, 0, 500), 300), Is.False);
        Assert.That(InteractRules.InReach(player, new Vector3(9000, 0, 0), 0), Is.True);
    }

    [Test]
    public void CubeBegin() {
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.Farming), Is.True);
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.Breeding), Is.True);
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.Nurturing), Is.False);
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.None), Is.False);
    }

    // Modes 4 and 5: i32 count, then a string id and a u8 per cube.
    [Test]
    public void CubeBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(1);
        writer.WriteUnicodeString("4_5_6");
        writer.WriteByte(1);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("4_5_6"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // A start replaces the kept names with the named objects the field has within reach, and a start
    // then an end of a gathered object pays once.
    [Test]
    public void StartKeepsReachable() {
        var started = new HashSet<string> { "old" };
        bool? Lookup(string name) => name switch { "near" => true, "far" => false, _ => null };
        InteractRules.TakeStarts(started, new[] { "near", "far", "gone", "near" }, Lookup);
        Assert.That(started, Is.EquivalentTo(new[] { "near" }));
        Assert.That(InteractRules.TakeEnds(started, new[] { "far", "near", "near" }, _ => (true, true)), Is.EqualTo(new[] { "near" }));
        Assert.That(started, Is.Empty);

        var names = new List<string>();
        for (int i = 0; i < InteractRules.MaxStarted + 10; i++) {
            names.Add("n" + i);
        }
        InteractRules.TakeStarts(started, names, _ => true);
        Assert.That(started, Has.Count.EqualTo(InteractRules.MaxStarted));
    }

    [Test]
    public void GatheredEndNeedsStart() {
        var started = new HashSet<string> { "a1" };
        (bool, bool)? Gathered(string name) => (true, true);
        // Each started name once, also when the packet repeats it.
        Assert.That(InteractRules.TakeEnds(started, new[] { "a1", "a1", "b2" }, Gathered), Is.EqualTo(new[] { "a1" }));
        Assert.That(started, Is.Empty);
        Assert.That(InteractRules.TakeEnds(started, new[] { "a1" }, Gathered), Is.Empty);
    }

    // Two chests in range: the start names the nearer one, the end the one the player clicked.
    [Test]
    public void TwoChests() {
        var started = new HashSet<string> { "near" };
        (bool, bool)? Chest(string name) => name switch {
            "near" or "far" => (false, true),
            "away" => (false, false),
            _ => null,
        };
        Assert.That(InteractRules.TakeEnds(started, new[] { "far" }, Chest), Is.EqualTo(new[] { "far" }));
        // No start at all (the nearer chest was used up): the chosen one still ends.
        Assert.That(InteractRules.TakeEnds(new HashSet<string>(), new[] { "far" }, Chest), Is.EqualTo(new[] { "far" }));
        Assert.That(InteractRules.TakeEnds(started, new[] { "away", "gone", "far", "far" }, Chest), Is.EqualTo(new[] { "far" }));
    }

    [Test]
    public void EndCap() {
        var names = new List<string>();
        for (int i = 0; i < InteractRules.MaxStarted + 10; i++) {
            names.Add("n" + i);
        }
        Assert.That(InteractRules.TakeEnds(new HashSet<string>(), names, _ => (false, true)), Has.Count.EqualTo(InteractRules.MaxStarted));
    }

    // The answers KMS2 gives per kind: a mesh object the result alone, a telescope, a UI object and a gathered
    // object the interaction and the result, the rest neither.
    [Test]
    public void AnswerPerKind() {
        Assert.That(InteractRules.Answer(InteractType.Mesh), Is.EqualTo((false, true)));
        Assert.That(InteractRules.Answer(InteractType.Telescope), Is.EqualTo((true, true)));
        Assert.That(InteractRules.Answer(InteractType.Ui), Is.EqualTo((true, true)));
        Assert.That(InteractRules.Answer(InteractType.Gathering), Is.EqualTo((true, true)));
        foreach (InteractType type in new[] { InteractType.Web, InteractType.DisplayImage, InteractType.GuildPoster, InteractType.BillBoard, InteractType.WatchTower }) {
            Assert.That(InteractRules.Answer(type), Is.EqualTo((false, false)));
        }
    }
}
