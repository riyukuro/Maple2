using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class SetCraftModeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestSetCraftMode;

    private enum Command : byte {
        Stop = 0,
        Start = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Stop:
                HandleStop(session, packet);
                return;
            case Command.Start:
                HandleStart(session, packet);
                return;
        }
    }

    private void HandleStop(GameSession session, IByteReader packet) {
        // The client writes the cube it holds for any mode (mypc.cpp:4175-4187), so a stop may carry
        // one. It is read and not used.
        CraftModeRules.ReadCube(packet);

        if (session.Field == null || session.GuideObject == null) {
            return;
        }

        session.Field.Broadcast(SetCraftModePacket.Stop(session.Player.ObjectId));
        session.Field.Broadcast(GuideObjectPacket.Remove(session.GuideObject));
        session.GuideObject = null;
        session.HeldCube = null;
    }

    private void HandleStart(GameSession session, IByteReader packet) {
        // The second byte says whether a cube follows: the client writes 0 and no cube when its palette
        // is empty (mypc.cpp:4151-4190), and then nothing is held and no guide is made.
        PlotCube? firstCubeItem = CraftModeRules.ReadCube(packet);
        if (firstCubeItem == null || session.Field == null || session.GuideObject != null) {
            return;
        }

        session.GuideObject = session.Field.SpawnGuideObject(session.Player, new ConstructionGuideObject());
        // KMS2 sends the guide with the player's heading: x and y 0, z the angle from 0 to 360. The client turns the
        // guide to it (CConstructionGuideObject init) and takes the place from the owner, not from the packet.
        float heading = MathF.Round(session.Player.Rotation.Z, 3) % 360f;
        session.GuideObject.Heading = new Vector3(0, 0, heading < 0 ? heading + 360f : heading);
        session.HeldCube = firstCubeItem;

        session.Field.Broadcast(GuideObjectPacket.Create(session.GuideObject));
        session.Field.Broadcast(SetCraftModePacket.Plot(session.Player.ObjectId, firstCubeItem));
    }
}
