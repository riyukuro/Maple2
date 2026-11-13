using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 12 (remove) and 14 (rotate) with no plot the player may build on are answered as the client answers a
// cube not the player's: 0x0C 40 s_ugcmap_cant_remove_default_cube, 0x0E 27 s_ugcmap_cant_rotate_default_cube.
// Before, nothing answered them. A rotate of an empty cell on the own plot is answered in the rotate's own shape
// (ResponseCube case 0xe: error, two object ids), not as a plot purchase answer.
public class RotateRemoveRefusalTests {
    private TestGameSession test = null!;

    private (GameSession Session, Plot Plot) StartInHome(bool own) {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        if (!own) {
            plot.OwnerId = test.AccountId + 1;
        }
        test.TakeSent();
        return (session, plot);
    }

    private void Send(GameSession session, byte command, bool rotate) {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.Write<Vector3B>(new Vector3B(-1, -1, 1));
            if (rotate) {
                writer.WriteBool(true);
            }
        }));
    }

    private void AssertAnswer(CubePacket.Edit edit, UgcMapError error, GameSession session) {
        byte[] answer = CubePacket.EditError(edit, error, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(answer) }));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    [Test]
    public void Remove_WithNoBuildPlot_IsRefusedWith40() {
        (GameSession session, _) = StartInHome(own: false);
        Send(session, 12, rotate: false);
        AssertAnswer(CubePacket.Edit.Remove, UgcMapError.s_ugcmap_cant_remove_default_cube, session);
    }

    [Test]
    public void Rotate_WithNoBuildPlot_IsRefusedWith27() {
        (GameSession session, _) = StartInHome(own: false);
        Send(session, 14, rotate: true);
        AssertAnswer(CubePacket.Edit.Rotate, UgcMapError.s_ugcmap_cant_rotate_default_cube, session);
    }

    [Test]
    public void Rotate_OfAnEmptyCell_IsAnsweredInTheRotateShape() {
        (GameSession session, Plot plot) = StartInHome(own: true);
        Send(session, 14, rotate: true);
        AssertAnswer(CubePacket.Edit.Rotate, UgcMapError.s_ugcmap_no_cube_to_rotate, session);
        Assert.That(plot.Cubes, Is.Empty);
    }

    [Test]
    public void EditErrorRotate_IsTheClientsShape() {
        byte[] body = CubePacket.EditError(CubePacket.Edit.Rotate, UgcMapError.s_ugcmap_cant_rotate_default_cube, 0x21).ToArray();
        Assert.That(Convert.ToHexString(body[2..]), Is.EqualTo("0E1B2100000021000000"));
    }
}
