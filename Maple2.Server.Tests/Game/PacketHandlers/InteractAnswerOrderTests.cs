using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The answers to an interaction in the order KMS2 sends them: the end of a mesh object and of a telescope, a
// gathered field object and a harvested cube.
public class InteractAnswerOrderTests {
    private const int RecipeId = 77;
    private const int RewardItemId = 30000001;

    private TestGameSession test = null!;
    private readonly InteractObjectHandler handler = new();

    private static InteractObjectMetadata Row(int id, InteractType type, int recipeId = 0, int reset = 0) {
        return new InteractObjectMetadata(id, type, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, recipeId), new InteractObjectMetadataTime(reset, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
    }

    // Experience for a telescope and a gather: 100 at every level.
    private void SetExpTables() {
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.telescope] = new(1, 1f), [ExpType.gathering] = new(1, 1f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
    }

    // The session stands in a field with a level (the experience of a telescope and a gather reads it).
    private GameSession Start() {
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Drop = new MapMetadataDrop(1, 0, []) });
        return session;
    }

    private void Send(GameSession session, byte command, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(1);
            writer.WriteString(name);
        }));
    }

    // The recipe of the gathers here: always a success at the first try, 10 mastery, one item and experience.
    private GameSession StartWithRecipe(MasteryType type, int questId = 0) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardItemId));
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry> {
            [RecipeId] = new(RecipeId, type, NoRewardExp: false, RequiredMastery: 0, RequiredMeso: 0, RequiredQuests: questId == 0 ? [] : [questId], RequiredLevel: 0, RewardExp: 0,
                RewardMastery: 10, HighRateLimitCount: 5, NormalRateLimitCount: 10, RequiredItems: [], HabitatMapId: [],
                RewardItems: [new ItemComponent(RewardItemId, 1, 1, ItemTag.None)]),
        }));
        SetExpTables();
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new MasteryDifferentialFactorTable(new Dictionary<int, MasteryDifferentialFactorTable.Entry> { [0] = new(0, 10000) }));
        GameSession session = Start();
        test.SetGatheringCounts();
        return session;
    }

    private static string Hex(byte[] packet) => Convert.ToHexString(packet);

    // The end of a mesh object: the object's new state (4), then the result (13) with code 0.
    [Test]
    public void MeshEnd_SendsTheStateThenTheResult() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.AddInteract(Row(11, InteractType.Mesh), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        test.TakeSent();

        Send(session, 12, "ab");

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.TakeSent().Select(Hex), Is.EqualTo(new[] {
            "6300" + "04" + "02006162" + "00" + "01",
            "6300" + "0D" + "00" + "02006162" + "01",
        }));
    }

    // The end of a telescope: the interaction (5), then the result (13) with code 0, on the first find and after;
    // no state update, the telescope stays usable.
    [Test]
    public void TelescopeEnd_SendsTheInteractionThenResultZero() {
        test = new TestGameSession();
        SetExpTables();
        GameSession session = Start();
        test.AddInteract(Row(12, InteractType.Telescope, reset: 1), new InteractTelescopeObject("ab", new Ms2Telescope(12, Vector3.Zero, Vector3.Zero)));
        Assert.That((byte) InteractResult.s_interact_find_new_telescope, Is.EqualTo(0));

        for (int i = 0; i < 2; i++) {
            test.TakeSent();
            Send(session, 12, "ab");

            Assert.That(test.Errors, Is.Empty);
            List<string> sent = test.TakeSent().Select(Hex).Where(packet => packet.StartsWith("6300")).ToList();
            Assert.That(sent, Is.EqualTo(new[] {
                "6300" + "05" + "02006162" + "02",
                "6300" + "0D" + "00" + "02006162" + "02",
            }));
        }
        Assert.That(session.Player.Value.Unlock.InteractedObjects, Does.Contain(12));
    }

    // A gather whose recipe asks a quest goes on only while the quest is in progress (the client's
    // CInteractGatheringObject checks IsQuestActive): a completed quest gives error 3 and no drop.
    [TestCase(QuestState.Completed, false)]
    [TestCase(QuestState.Started, true)]
    public void FieldGather_NeedsTheRecipesQuestInProgress(QuestState state, bool gathers) {
        const int questId = 90000600;
        GameSession session = StartWithRecipe(MasteryType.Mining, questId);
        test.SetQuest(questId, state);
        test.AddInteract(Row(13, InteractType.Gathering, RecipeId, reset: 1000), new InteractGatheringObject("ab", new Ms2InteractActor(13, Vector3.Zero, Vector3.Zero)));
        Send(session, 11, "ab");
        test.TakeSent();

        Send(session, 12, "ab");

        Assert.That(test.Errors, Is.Empty);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Any(packet => TestGameSession.Op(packet) == SendOp.FieldAddItem), Is.EqualTo(gathers));
        bool refused = sent.Any(packet => TestGameSession.Op(packet) == SendOp.Mastery && packet[2] == 3
            && BitConverter.ToInt16(packet, 3) == (short) MasteryError.s_mastery_error_lack_quest);
        Assert.That(refused, Is.EqualTo(!gathers));
    }

    // A gathered field object: the mastery, the drop, the interaction (5) and the result (13), then the experience.
    [Test]
    public void FieldGather_SendsMasteryDropInteractionResultExp() {
        GameSession session = StartWithRecipe(MasteryType.Mining);
        test.AddInteract(Row(13, InteractType.Gathering, RecipeId, reset: 1000), new InteractGatheringObject("ab", new Ms2InteractActor(13, Vector3.Zero, Vector3.Zero)));
        Send(session, 11, "ab");
        test.TakeSent();

        Send(session, 12, "ab");

        Assert.That(test.Errors, Is.Empty);
        List<byte[]> sent = test.TakeSent();
        SendOp[] order = [SendOp.Mastery, SendOp.FieldAddItem, SendOp.InteractObject, SendOp.InteractObject, SendOp.ExpUp];
        Assert.That(sent.Select(TestGameSession.Op).Where(op => order.Contains(op)), Is.EqualTo(order));
        Assert.That(sent.Select(Hex).Where(packet => packet.StartsWith("6300")), Is.EqualTo(new[] {
            "6300" + "05" + "02006162" + "06" + "0000" + "01000000",
            "6300" + "0D" + "00" + "02006162" + "06",
        }));
    }

    // A harvested cube: the mastery, the drop, the cube's update (3) and the success (9), then the experience.
    [Test]
    public void CubeHarvest_SendsMasteryDropUpdateSuccessExp() {
        GameSession session = StartWithRecipe(MasteryType.Farming);
        var cube = new InteractCube(new Vector3B(1, 2, 3), new FunctionCubeMetadata(5, RecipeId, ConfigurableCubeType.None,
            InteractCubeState.Available, InteractCubeControlType.Farming, [], 0, null));
        test.AddFunctionCube(cube);
        var cubeHandler = new FunctionCubeHandler {
            FunctionCubeMetadataStorage = session.FunctionCubeMetadata, ServerTableMetadataStorage = test.ServerTableMetadata,
        };
        foreach (byte command in new byte[] { 4, 5 }) {
            test.TakeSent();
            cubeHandler.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(command);
                writer.WriteInt(1);
                writer.WriteUnicodeString(cube.Id);
                writer.WriteByte(0);
            }));
        }

        Assert.That(test.Errors, Is.Empty);
        List<byte[]> sent = test.TakeSent();
        SendOp[] order = [SendOp.Mastery, SendOp.FieldAddItem, SendOp.FunctionCube, SendOp.FunctionCube, SendOp.ExpUp];
        Assert.That(sent.Select(TestGameSession.Op).Where(op => order.Contains(op)), Is.EqualTo(order));
        Assert.That(sent.Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube).Select(packet => packet[2]), Is.EqualTo(new byte[] { 3, 9 }));
    }
}
