using System;
using System.Numerics;
using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A portal out of the return field takes the spot it was used at as the return field's spot (02000043's hidden
// portal 4 into the solo quest instance 52000037): a logout in the instance comes back there, not to the far spawn.
public class ReturnSpotPortalTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int ReturnMap = 2000043;
    private const int Instance = 52000037;

    private static (TestGameSession Test, FieldManager Field) InTheReturnField(Vector3 spot, PortalActionType actionType) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(ReturnMap));
        FieldManager field = session.Field!;
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(field, Activator.CreateInstance(portals.FieldType));
        field.SpawnPortal(new Portal(4, Instance, 1, PortalType.Field, actionType, new Vector3(1350, -1275, 2575), Vector3.Zero,
            new Vector3(200, 350, 250), 0, 0, true, true, true));
        Character character = session.Player.Value.Character;
        character.ReturnMaps.Push(ReturnMap);
        character.ReturnPosition = spot;
        session.Player.Position = new Vector3(1350, -1275, 2575);
        test.TakeSent();
        return (test, field);
    }

    // The field cannot be made (the channel has no such map): the player stays and the spot there was stays.
    [Test]
    public void RefusedMove_KeepsTheSpotThereWas() {
        (TestGameSession test, FieldManager field) = InTheReturnField(new Vector3(1, 2, 3), PortalActionType.Interact);
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        void Set(string name, object value) => typeof(FieldManager.Factory).GetField(name, Any)!.SetValue(factory, value);
        Set("mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        Set("fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>>());
        Set("<ServerTableMetadata>k__BackingField", test.ServerTableMetadata);
        Set("<MapMetadata>k__BackingField", test.MapMetadata);
        Set("logger", Serilog.Log.Logger);
        typeof(GameSession).GetField("<FieldFactory>k__BackingField", Any)!.SetValue(test.Session, factory);

        field.UsePortal(test.Session, 4, "");

        Assert.That(test.Session.Player.Value.Character.ReturnPosition, Is.EqualTo(new Vector3(1, 2, 3)));
    }
}
