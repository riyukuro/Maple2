using System;
using System.Collections.Generic;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Validators;

// The tutorial clear (RecvOp Tutorial) opens job.xml's tutorialClearOpenMaps and tutorialClearOpenTaxis, only in
// the job's start field. The KR rows for the jobs that start in 52000065.
public class TutorialRulesTests {
    private const int StartField = 52000065;
    private static readonly int[] OpenMaps = { 2000062, 2000114, 2000115, 2000116, 2000117, 2000118 };
    private static readonly int[] OpenTaxis = { 2000062 };

    [Test]
    public void TheClearOpensTheMapsAndTaxisAndReturnsOnlyTheNewMaps() {
        var maps = new SortedSet<int> { StartField, 2000062 };
        var taxis = new SortedSet<int>();
        List<int> added = TutorialRules.Clear(StartField, StartField, OpenMaps, OpenTaxis, maps, taxis);
        Assert.That(added, Is.EqualTo(new[] { 2000114, 2000115, 2000116, 2000117, 2000118 }));
        Assert.That(maps, Is.SupersetOf(OpenMaps));
        Assert.That(taxis, Is.EquivalentTo(OpenTaxis));

        // The guide step can send it again: nothing new.
        Assert.That(TutorialRules.Clear(StartField, StartField, OpenMaps, OpenTaxis, maps, taxis), Is.Empty);
        Assert.That(maps.Count, Is.EqualTo(7));
    }

    [Test]
    public void OutsideTheStartFieldNothingOpens() {
        var maps = new SortedSet<int>();
        var taxis = new SortedSet<int>();
        Assert.That(TutorialRules.Clear(StartField, 2000062, OpenMaps, OpenTaxis, maps, taxis), Is.Empty);
        Assert.That(TutorialRules.Clear(0, 0, OpenMaps, OpenTaxis, maps, taxis), Is.Empty);
        Assert.That(maps, Is.Empty);
        Assert.That(taxis, Is.Empty);
    }

    [Test]
    public void AJobWithNoListsOpensNothing() {
        // Striker (start field 63000015) lists no maps or taxis.
        var maps = new SortedSet<int>();
        var taxis = new SortedSet<int>();
        Assert.That(TutorialRules.Clear(63000015, 63000015, Array.Empty<int>(), Array.Empty<int>(), maps, taxis), Is.Empty);
        Assert.That(maps, Is.Empty);
    }

    // FindFields 0x81: int count, then the map ids.
    [Test]
    public void FindFieldsBytes() {
        byte[] bytes = FindFieldsPacket.Add(new List<int> { 2000114, 2000115 }).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 4 + 8));
        Assert.That(BitConverter.ToUInt16(bytes, 0), Is.EqualTo(0x81));
        Assert.That(BitConverter.ToInt32(bytes, 2), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt32(bytes, 6), Is.EqualTo(2000114));
        Assert.That(BitConverter.ToInt32(bytes, 10), Is.EqualTo(2000115));
    }
}
