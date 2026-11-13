using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The cast in progress (Fishing mode 9) and a field change: the client leaves its fishing state on the move, so a
// catch (mode 8) sent in the next field without a new start must not land the old cast's fish.
public class FishingCastTests {
    private static FishTable.Fish Fish() => new(10000001, LiquidType.water, 80, 6, 1, 3, 1, 54, 15000, 10000, 8000,
        new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);

    [Test]
    public void LeavingTheField_EndsTheCast() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        var tile = (FishingTile) RuntimeHelpers.GetUninitializedObject(typeof(FishingTile));
        session.Fishing.Cast(tile, Fish(), fishBites: true, fishBiteTick: 0, fightGame: false);
        Assert.That(session.Fishing.Casting, Is.True);

        // What a started session holds and the leave clears: the test session is made without its initializers.
        foreach (string name in new[] { nameof(GameSession.InteractStarted), nameof(GameSession.CubeStarted) }) {
            typeof(GameSession).GetField(name)!.SetValue(session, new HashSet<string>());
        }
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging))!.SetValue(session, new long[18]);
        typeof(GameSession).GetField(nameof(GameSession.DismantleStaging))!.SetValue(session, Array.CreateInstance(typeof(GameSession).GetField(nameof(GameSession.DismantleStaging))!.FieldType.GetElementType()!, 0));
        // The field's own removal of the player is not under test: the session's state is cleared before it.
        session.Field = null;
        typeof(GameSession).GetMethod("LeaveField", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);

        Assert.That(session.Fishing.Casting, Is.False);
        // The next field (map 0, no fishing spot): a cast kept over the move would get past the cast check to the
        // spot lookup and answer notexist_fish; with the cast ended the catch stops at the cast check.
        session.Field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        Assert.That(session.Fishing.Catch(true), Is.EqualTo(FishingError.s_fishing_error_system_error));
    }
}
