using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nurturing = Maple2.Model.Game.Nurturing;
using UgcMap = Maple2.Database.Model.UgcMap;
using UgcMapCube = Maple2.Database.Model.UgcMapCube;

namespace Maple2.Server.Tests.Database;

// The daily record of housing pets through GameStorage, on an in-memory database. Plots and placed cubes are
// written straight to the context: the storage calls that make them need item metadata.
public class NurturingDailyStorageTests {
    private const long Me = 1;
    private const long Other = 2;
    private const long Third = 3;
    private const int Cat = 100;
    private const int Devil = 101;

    private static readonly FunctionCubeMetadata.NurturingData Data = new(default, default,
        [new FunctionCubeMetadata.NurturingData.Growth(100, 1, default), new FunctionCubeMetadata.NurturingData.Growth(200, 2, default)], "tag");

    private DbContextOptions options = null!;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private static InteractCube Pet(int itemId) {
        return new InteractCube(new Vector3B(1, 2, 3), new FunctionCubeMetadata(itemId, 0, default, default, InteractCubeControlType.Nurturing, [], 0, Data));
    }

    // A pet of `ownerId` with its row, as it is after the cube was placed.
    private InteractCube Place(long ownerId, int itemId, params long[] cubeIds) {
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateNurturing(ownerId, Data, itemId), Is.Not.Null);
        }
        using (var context = new Ms2Context(options)) {
            var map = new UgcMap { OwnerId = ownerId, MapId = 1, Number = (int) ownerId * 1000 + itemId };
            context.UgcMap.Add(map);
            context.SaveChanges();
            foreach (long cubeId in cubeIds) {
                context.UgcMapCube.Add(new UgcMapCube { Id = cubeId, UgcMapId = map.Id, ItemId = itemId });
            }
            context.SaveChanges();
        }

        return Pet(itemId);
    }

    private void Feed(InteractCube cube) {
        cube.Nurturing!.Feed(10);
        using GameStorage.Request db = storage.Context();
        Assert.That(db.RecordNurturing(Me, Me, cube, played: false), Is.True);
    }

    private void Play(long accountId, long ownerId, InteractCube cube) {
        Assert.That(cube.Nurturing!.Play(accountId, 10, 3), Is.True);
        using GameStorage.Request db = storage.Context();
        Assert.That(db.RecordNurturing(accountId, ownerId, cube, played: true), Is.True);
    }

    private IList<NurturingRecord> Daily(long accountId, bool played) {
        using GameStorage.Request db = storage.Context();
        return db.GetNurturingDaily(accountId, played);
    }

    private int Count(long accountId, bool played) {
        using GameStorage.Request db = storage.Context();
        return db.CountNurturingDaily(accountId, played);
    }

    [Test]
    public void NoRows_EmptyRecord() {
        Assert.That(Daily(Me, played: false), Is.Empty);
        Assert.That(Daily(Me, played: true), Is.Empty);
        Assert.That(Count(Me, played: false), Is.EqualTo(0));
    }

    // The client counts the fed list for the daily feed cap, so each feeding is an entry of its own.
    [Test]
    public void Feedings_OneEntryEach() {
        InteractCube cat = Place(Me, Cat, 500);
        InteractCube devil = Place(Me, Devil, 501);
        Feed(cat);
        Feed(devil);
        Feed(cat);

        Assert.That(Count(Me, played: false), Is.EqualTo(3));
        Assert.That(Daily(Me, played: false), Is.EqualTo(new[] {
            new NurturingRecord(Me, Cat, 500), new NurturingRecord(Me, Devil, 501), new NurturingRecord(Me, Cat, 500),
        }));
        Assert.That(Count(Me, played: true), Is.EqualTo(0));
        Assert.That(Daily(Me, played: true), Is.Empty);
    }

    // The pet row is saved with the daily row.
    [Test]
    public void Feeding_SavesThePet() {
        InteractCube cat = Place(Me, Cat, 500);
        Feed(cat);

        using GameStorage.Request db = storage.Context();
        Nurturing? saved = db.GetNurturing(Me, Cat, Data);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Exp, Is.EqualTo(10));
    }

    // Only the rows of the asking account are read: the plays of other accounts with the same pets are not in it.
    [Test]
    public void Plays_OnlyOfTheAccount() {
        InteractCube otherCat = Place(Other, Cat, 600);
        InteractCube thirdCat = Place(Third, Cat, 700);
        InteractCube thirdDevil = Place(Third, Devil, 701);
        Play(Me, Other, otherCat);
        Play(Third, Other, otherCat);
        Play(Me, Third, thirdDevil);
        Play(Other, Third, thirdCat);

        Assert.That(Count(Me, played: true), Is.EqualTo(2));
        Assert.That(Daily(Me, played: true), Is.EqualTo(new[] {
            new NurturingRecord(Other, Cat, 600), new NurturingRecord(Third, Devil, 701),
        }));
        Assert.That(Daily(Other, played: true), Is.EqualTo(new[] { new NurturingRecord(Third, Cat, 700) }));
        Assert.That(Count(Me, played: false), Is.EqualTo(0));

        using GameStorage.Request db = storage.Context();
        Assert.That(db.GetNurturing(Other, Cat, Data)!.PlayedBy, Is.EqualTo(new[] { Me, Third }));
    }

    // The same item of two owners keeps its own cube; an item placed twice lists the lowest uid; a pet that is
    // not placed any more has uid 0.
    [Test]
    public void CubeIds_PerOwnerAndItem() {
        InteractCube otherCat = Place(Other, Cat, 620, 610);
        InteractCube thirdCat = Place(Third, Cat, 700);
        InteractCube thirdDevil = Place(Third, Devil);
        Play(Me, Other, otherCat);
        Play(Me, Third, thirdCat);
        Play(Me, Third, thirdDevil);

        Assert.That(Daily(Me, played: true).Select(record => record.CubeId), Is.EqualTo(new long[] { 610, 700, 0 }));
    }

    [Test]
    public void Record_NoPetRow_Fails() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.RecordNurturing(Me, Other, Pet(Cat), played: true), Is.False);
        Assert.That(db.CountNurturingDaily(Me, played: true), Is.EqualTo(0));
    }
}
