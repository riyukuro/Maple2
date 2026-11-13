using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// FunctionCube 0x002E mode 5 on a nest: the client feeds or plays with a nest only when its quest tag is the
// NurturingQuestTag constant and that term tag runs (functioncubeobject.cpp:1934-1937), and only within
// MasteryObjectInteractionDistance of the character (statenurturing.cpp:200-201). The owner here has no feed, so a
// use the server takes is answered with the nurturing error 1 (no feed); a use it does not take gets no answer.
public class NurturingNestGateTests {
    private const int CubeId = 50800010;
    private const int TermId = 153;
    private const string Tag = "NurturingGhostCats";
    private const float Reach = 300;

    private TestGameSession test = null!;

    private static FunctionCubeMetadata.NurturingData Data(string tag) => new(new RewardItem(30001433, 1, 1), default,
        [new FunctionCubeMetadata.NurturingData.Growth(100, 1, default)], tag);

    // The owner stands in their home next to a nest with the given quest tag; the term of Tag runs or has ended.
    private (GameSession Session, FieldFunctionInteract Nest) Start(string tag, bool termRunning, float distance) {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterHome();
        test.SetServerTable(new ConstantsTable(MasteryObjectInteractionDistance: (int) Reach, NurturingQuestTag: Tag));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        test.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> {
            [TermId] = new(TermId, Tag, now - 3600, termRunning ? now + 3600 : now - 60),
        }));
        FunctionCubeMetadata.NurturingData data = Data(tag);
        using (GameStorage.Request db = test.Storage.Context()) {
            db.CreateNurturing(test.AccountId, data, CubeId);
        }
        var cube = new InteractCube(new Vector3B(0, 0, 0), new FunctionCubeMetadata(CubeId, 0, ConfigurableCubeType.None,
            InteractCubeState.Available, InteractCubeControlType.Nurturing, [], 0, data));
        FieldFunctionInteract nest = test.AddFunctionCube(cube);
        nest.Position = session.Player.Position + new Vector3(distance, 0, 0);
        return (session, nest);
    }

    private List<byte[]> Use(GameSession session, FieldFunctionInteract nest) {
        var handler = new FunctionCubeHandler {
            FunctionCubeMetadataStorage = session.FunctionCubeMetadata,
            ServerTableMetadataStorage = test.ServerTableMetadata,
        };
        test.TakeSent();
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
            writer.WriteUnicodeString(nest.InteractCube.Id);
            writer.WriteByte(0);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube).ToList();
    }

    [TestCase(Tag, true, 0f, true, TestName = "Nest_TagTermAndReach_Taken")]
    [TestCase("NurturingPumpkinDevil", true, 0f, false, TestName = "Nest_OtherQuestTag_NotTaken")]
    [TestCase(Tag, false, 0f, false, TestName = "Nest_TermEnded_NotTaken")]
    [TestCase(Tag, true, Reach + 1000f, false, TestName = "Nest_OutOfReach_NotTaken")]
    public void Use_TakenOnlyForTheTermNestInReach(string tag, bool termRunning, float distance, bool taken) {
        (GameSession session, FieldFunctionInteract nest) = Start(tag, termRunning, distance);

        List<byte[]> sent = Use(session, nest);

        Assert.That(sent.Select(packet => packet[2]), Is.EqualTo(taken ? new byte[] { 13 } : Array.Empty<byte>()));
    }
}
