using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Tools.VectorMath;
using Maple2.Model.Game.Field;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Metadata;

// The map-data blob starts with a marker and a format version; a blob of another layout stops with "Rerun Ingest!".
public class MapDataFormatTests {
    private static byte[] Blob() {
        var writer = new ByteWriter();
        writer.WriteClass(new FieldAccelerationStructure());
        return writer.ToArray();
    }

    [Test]
    public void CurrentFormatReads() {
        byte[] blob = Blob();
        var reader = new ByteReader(blob, 0);
        Assert.Multiple(() => {
            Assert.That(BitConverter.ToInt32(blob, 0), Is.EqualTo(FieldAccelerationStructure.FormatMarker));
            Assert.That(BitConverter.ToInt32(blob, 4), Is.EqualTo(FieldAccelerationStructure.FormatVersion));
            Assert.DoesNotThrow(() => reader.ReadClassWithNew<FieldAccelerationStructure>());
            Assert.That(reader.Position, Is.EqualTo(blob.Length));
        });
    }

    // A vibrate object keeps its materialTag through the map data (the vibrate_object condition code).
    [Test]
    public void VibrateMaterialReads() {
        var tree = new FieldVibrateEntity(new FieldEntityId(1, 2, "tree"), Vector3.Zero, Vector3.Zero, 1,
            new BoundingBox3(), BreakDefense: 99, BreakTick: 0, VibrateIndex: 0, MaterialTag: "wood");
        var structure = new FieldAccelerationStructure();
        structure.AddEntities(new Dictionary<Vector3S, List<FieldEntity>>(), new Vector3S(short.MaxValue, 0, 0),
            new Vector3S(0, 0, 0), [tree], 1);
        var writer = new ByteWriter();
        writer.WriteClass(structure);

        var read = new ByteReader(writer.ToArray(), 0).ReadClassWithNew<FieldAccelerationStructure>();
        Assert.That(read.GetVibrateEntity("tree")?.MaterialTag, Is.EqualTo("wood"));
    }

    // A blob from the older ingest starts with the grid size: it has no marker.
    [Test]
    public void UnmarkedBlobStops() {
        byte[] old = Blob().Skip(8).ToArray();
        var reader = new ByteReader(old, 0);
        var error = Assert.Throws<InvalidOperationException>(() => reader.ReadClassWithNew<FieldAccelerationStructure>());
        Assert.That(error!.Message, Does.Contain("Rerun Ingest!"));
    }

    [Test]
    public void OtherVersionStops() {
        byte[] blob = Blob();
        BitConverter.GetBytes(FieldAccelerationStructure.FormatVersion + 1).CopyTo(blob, 4);
        var error = Assert.Throws<InvalidOperationException>(() => FieldAccelerationStructure.ReadFormat(new ByteReader(blob, 0)));
        Assert.That(error!.Message, Does.Contain($"format {FieldAccelerationStructure.FormatVersion + 1}").And.Contain("Rerun Ingest!"));
    }

    // Read as the old layout's grid size (two shorts), the marker is negative in X and Y, which no map has.
    [Test]
    public void MarkerIsNoGridSize() {
        byte[] marker = BitConverter.GetBytes(FieldAccelerationStructure.FormatMarker);
        Assert.Multiple(() => {
            Assert.That(BitConverter.ToInt16(marker, 0), Is.Negative);
            Assert.That(BitConverter.ToInt16(marker, 2), Is.Negative);
        });
    }
}
