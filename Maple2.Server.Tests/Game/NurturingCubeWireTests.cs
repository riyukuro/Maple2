using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UgcMap = Maple2.Database.Model.UgcMap;

namespace Maple2.Server.Tests.Game;

// FunctionCube 0x004D modes 2 and 3 for a nest. The client reads a cube record as its id, the state int, the cube's
// own data and a byte (CFunctionCubeService::ReadCubeState, functioncubeobject.cpp:2604); a nest cube
// (CNurturingCubeObject::vfunc_1, functioncubeobject.cpp:1784) reads its pet as two longs, three shorts and a long
// after the state. The pet row is keyed by the cube's function cube id, not by the item the cube is placed from.
public class NurturingCubeWireTests {
    private const int ItemId = 50400341;
    private const int CubeId = 50800010;
    private const long Owner = 9;

    private static readonly FunctionCubeMetadata.NurturingData Data = new(new RewardItem(30001433, 1, 1), default,
        [new FunctionCubeMetadata.NurturingData.Growth(100, 1, default), new FunctionCubeMetadata.NurturingData.Growth(200, 2, default)],
        "NurturingGhostCats");

    private static readonly FunctionCubeMetadata Cube = new(CubeId, 0, ConfigurableCubeType.None, InteractCubeState.Available,
        InteractCubeControlType.Nurturing, [], 0, Data);

    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static byte[] Record(string id, params Action<ByteWriter>[] parts) {
        var writer = new ByteWriter();
        writer.WriteByte(3);
        writer.WriteUnicodeString(id);
        writer.WriteInt((int) InteractCubeState.Available);
        foreach (Action<ByteWriter> part in parts) {
            part(writer);
        }
        writer.WriteByte(0);
        return writer.Buffer.AsSpan(0, writer.Length).ToArray();
    }

    [Test]
    public void Add_NestCarriesItsPet() {
        var cube = new InteractCube(new Vector3B(1, 2, 3), Cube) {
            Nurturing = new Nurturing(150, 2, [], DateTimeOffset.FromUnixTimeSeconds(1700000000),
                DateTimeOffset.FromUnixTimeSeconds(1700003600), Data),
        };

        byte[] expected = Record(cube.Id, writer => {
            writer.WriteLong(1700000000);
            writer.WriteLong(150);
            writer.WriteShort(2);
            writer.WriteShort(2);
            writer.WriteShort(0);
            writer.WriteLong(1700003600);
        });

        Assert.That(Body(FunctionCubePacket.AddFunctionCube(cube)), Is.EqualTo(expected));
    }

    // A nest read back from its cube row before its pet is attached (the record the client threw on).
    [Test]
    public void Add_NestWithoutItsPetRow_StillSendsAPet() {
        var cube = new InteractCube("4_130559", Cube, null, null);

        byte[] body = Body(FunctionCubePacket.AddFunctionCube(cube));

        Assert.That(body.Length, Is.EqualTo(Record(cube.Id).Length + 30));
        Assert.That(BitConverter.ToInt16(body, Record(cube.Id).Length - 1 + 16), Is.EqualTo(1), "stage");
    }

    [Test]
    public void Add_OtherCube_HasNoPet() {
        var cube = new InteractCube(new Vector3B(1, 2, 3), Cube with { ControlType = InteractCubeControlType.None, Nurturing = null });

        Assert.That(Body(FunctionCubePacket.AddFunctionCube(cube)), Is.EqualTo(Record(cube.Id)));
    }

    // The place path: the cube made from the item reads back with the pet row made for its function cube id.
    [Test]
    public void CreateCube_PlacedNestReadsBackItsPet() {
        (GameStorage storage, DbContextOptions options) = Storage();
        var map = new UgcMap { OwnerId = Owner, MapId = 1, Number = 1 };
        using (var context = new Ms2Context(options)) {
            context.UgcMap.Add(map);
            context.SaveChanges();
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateNurturing(Owner, Data, CubeId), Is.Not.Null);
        }
        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = map.Id;
        plot.OwnerId = Owner;
        var placed = new PlotCube(Item()) {
            Position = new Vector3B(1, 2, 3),
            Type = PlotCube.CubeType.Construction,
            Interact = new InteractCube(new Vector3B(1, 2, 3), Cube),
        };

        PlotCube? cube;
        using (GameStorage.Request db = storage.Context()) {
            cube = db.CreateCube(plot, placed);
        }

        Assert.That(cube?.Interact?.Nurturing, Is.Not.Null);
        Assert.That(cube!.Interact!.Nurturing!.ClaimedGiftForStage, Is.EqualTo(1));
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.LoadCubesForOwner(Owner)[0].Interact?.Nurturing, Is.Not.Null, "field-entry load");
        }
    }

    private static ItemMetadata Item() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(ItemId, null, [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, new ItemMetadataHousing(0, 0, 0, default, true, 3), null);
    }

    private static (GameStorage, DbContextOptions) Storage() {
        var items = (ItemMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ItemMetadataStorage));
        var cache = new LRUCache<int, ItemMetadata>(16, 1);
        cache.AddReplace(ItemId, Item());
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(items, cache);
        var cubes = (FunctionCubeMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(FunctionCubeMetadataStorage));
        typeof(FunctionCubeMetadataStorage).GetField("FunctionCubeMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cubes, new Dictionary<int, FunctionCubeMetadata> { [CubeId] = Cube });
        DbContextOptions options = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return (new GameStorage(options, items, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, cubes), options);
    }
}
