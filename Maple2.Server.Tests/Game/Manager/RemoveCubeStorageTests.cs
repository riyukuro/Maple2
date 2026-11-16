using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// Taking a placed cube off the plot gives it back to the owner's furnishing storage in one write: the cube leaves the
// plot and its row, the storage stack is one higher in memory and in the store, and a removal that cannot be stored
// leaves the cube placed with the stack as it was. A position with no cube answers an error and changes nothing.
public class RemoveCubeStorageTests {
    private const int CubeItemId = 50200002;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.Register(TestGameSession.Metadata(CubeItemId));
        test.Start();
        session.Housing.InitNewHome("home", null);
        plot = test.EnterHome();
        session.Field!.Players.Clear();
        typeof(GameSession).GetField("<SeatRelease>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, new SeatReleaseEcho());
        test.TakeSent();
    }

    // A placed cube as a place leaves it: its plot row, and held in the owner's furnishing inventory.
    private PlotCube Place(Vector3B position, out long itemUid) {
        itemUid = session.Item.Furnishing.AddCube(CubeItemId);
        var cube = new PlotCube(TestGameSession.Metadata(CubeItemId)) { Position = position, Type = PlotCube.CubeType.Construction };
        using (GameStorage.Request db = test.Storage.Context()) {
            cube = db.CreateCube(plot, cube)!;
        }
        cube.Position = position;
        Assert.That(session.Item.Furnishing.TryAddCube(itemUid, cube), Is.True);
        plot.Cubes[position] = cube;
        test.TakeSent();
        return cube;
    }

    private int Stored(long itemUid) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetItem(itemUid)?.Amount ?? -1;
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();
    private string Error(UgcMapError error) =>
        Convert.ToHexString(CubePacket.EditError(CubePacket.Edit.Remove, error, session.Player.ObjectId).ToArray());

    [Test]
    public void Remove_GivesTheCubeBack_InMemoryAndInTheStore() {
        var position = new Vector3B(-1, -1, 1);
        PlotCube cube = Place(position, out long uid);
        int held = session.Item.Furnishing.GetItem(CubeItemId)!.Amount;

        Assert.That(session.Housing.TryRemoveCube(plot, position, out PlotCube? removed), Is.True);

        Assert.That(removed, Is.SameAs(cube));
        Assert.That(plot.Cubes, Does.Not.ContainKey(position));
        Assert.That(session.Item.Furnishing.GetItem(CubeItemId)!.Amount, Is.EqualTo(held + 1));
        Assert.That(Stored(uid), Is.EqualTo(held + 1));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCube(cube.Id), Is.Null);
    }

    [Test]
    public void Remove_OfAPositionWithNoCube_AnswersAnError_AndChangesNothing() {
        Place(new Vector3B(-1, -1, 1), out long uid);
        int held = session.Item.Furnishing.GetItem(CubeItemId)!.Amount;
        int storedBefore = Stored(uid);

        Assert.That(session.Housing.TryRemoveCube(plot, new Vector3B(-2, -2, 1), out _), Is.False);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_no_cube_to_remove) }));
        Assert.That(plot.Cubes, Contains.Key(new Vector3B(-1, -1, 1)));
        Assert.That(session.Item.Furnishing.GetItem(CubeItemId)!.Amount, Is.EqualTo(held));
        Assert.That(Stored(uid), Is.EqualTo(storedBefore));
    }

    [Test]
    public void Remove_TwiceAtTheSamePosition_GivesTheCubeBackOnce() {
        var position = new Vector3B(-1, -1, 1);
        Place(position, out long uid);
        int held = session.Item.Furnishing.GetItem(CubeItemId)!.Amount;

        session.Housing.TryRemoveCube(plot, position, out _);
        test.TakeSent();
        Assert.That(session.Housing.TryRemoveCube(plot, position, out _), Is.False);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_no_cube_to_remove) }));
        Assert.That(session.Item.Furnishing.GetItem(CubeItemId)!.Amount, Is.EqualTo(held + 1));
        Assert.That(Stored(uid), Is.EqualTo(held + 1));
    }

    [Test]
    public void Remove_WhenTheWriteFails_LeavesTheCubePlacedAndTheStackAsItWas() {
        var position = new Vector3B(-1, -1, 1);
        PlotCube cube = Place(position, out long uid);
        int held = session.Item.Furnishing.GetItem(CubeItemId)!.Amount;
        int storedBefore = Stored(uid);
        test.FailSave = _ => true;

        bool removed = session.Housing.TryRemoveCube(plot, position, out _);
        test.FailSave = null;

        Assert.That(removed, Is.False);
        Assert.That(Sent(), Does.Contain(Error(UgcMapError.s_ugcmap_db)));
        Assert.That(plot.Cubes[position], Is.SameAs(cube));
        Assert.That(session.Item.Furnishing.GetItem(CubeItemId)!.Amount, Is.EqualTo(held));
        Assert.That(Stored(uid), Is.EqualTo(storedBefore));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCube(cube.Id), Is.Not.Null);
    }
}
