using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Maple2.Database.Context;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Database.Storage;

public class MapDataStorage(MetadataContext context) : MetadataStorage<string, FieldAccelerationStructure>(context, CACHE_SIZE) {
    private const int CACHE_SIZE = 1500; // ~1.1k total Maps

    public bool TryGet(string xblock, [NotNullWhen(true)] out FieldAccelerationStructure? mapData) {
        if (Cache.TryGet(xblock, out mapData)) {
            return true;
        }

        lock (Context) {
            // Double-checked locking
            if (Cache.TryGet(xblock, out mapData)) {
                return true;
            }

            MapDataMetadata? data = Context.MapDataMetadata.Find(xblock);

            if (data == null) {
                return false;
            }

            mapData = Decode(data).ReadClassWithNew<FieldAccelerationStructure>();

            Cache.AddReplace(xblock, mapData);
        }

        return true;
    }

    /// <summary>
    /// Reads the format of one map's data at startup, so map data from an ingest of another layout stops the server
    /// with "Rerun Ingest!" instead of being misread when a field opens.
    /// </summary>
    public void CheckFormat() {
        lock (Context) {
            MapDataMetadata? data = Context.MapDataMetadata.FirstOrDefault();
            if (data == null) {
                throw new InvalidOperationException("No map data. Rerun Ingest!");
            }

            FieldAccelerationStructure.ReadFormat(Decode(data));
        }
    }

    private static ByteReader Decode(MapDataMetadata data) {
        var input = new MemoryStream(data.Data);
        var output = new MemoryStream();
        using (var dstream = new DeflateStream(input, CompressionMode.Decompress)) {
            dstream.CopyTo(output);
        }

        return new ByteReader(output.ToArray());
    }
}
