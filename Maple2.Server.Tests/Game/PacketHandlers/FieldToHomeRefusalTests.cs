using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The door of an outdoor plot (a FieldToHome portal) whose move the World server refuses: the client shows its move
// error box (CNetworkService::OnGameToGame) and stays in the field. The portal's own target, the home map, was
// entered on this server after the refusal, which took the visitor into their own home.
public class FieldToHomeRefusalTests : TriggerTestBase {
    [Test]
    public void ARefusedHomeVisit_StaysInTheField() {
        Start();
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, Activator.CreateInstance(portals.FieldType));
        FieldPortal portal = Field.SpawnPortal(new Portal(2, Constant.DefaultHomeMapId, -1, PortalType.FieldToHome, PortalActionType.Interact,
            Vector3.Zero, Vector3.Zero, new Vector3(75, 75, 75), 0, 0, true, false, true));
        portal.HomeId = test.Session.AccountId;
        test.World.MoveError = new RpcException(new Status(StatusCode.Unavailable, "refused"));
        FieldManager field = Field;
        int mapBefore = test.Session.Player.Value.Character.MapId;
        test.TakeSent();

        bool used = Field.UsePortal(test.Session, 2, "");

        Assert.Multiple(() => {
            Assert.That(used, Is.True);
            Assert.That(test.World.Moves, Has.Count.EqualTo(1), "the move asked for");
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server).ToArray()) }));
            Assert.That(test.Session.Field, Is.SameAs(field));
            Assert.That(test.Session.Player.Value.Character.MapId, Is.EqualTo(mapBefore));
        });
    }
}
