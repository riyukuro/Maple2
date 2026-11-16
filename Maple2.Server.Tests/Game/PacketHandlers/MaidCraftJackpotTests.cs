using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A maid craft's jackpot: the server pack's table/Server/MaidRecipeSvr.xml names, per recipe, an item a receive gives
// instead of the product at JackpotRate, times the maid grade's JackpotRate of table/Server/MaidGradeInfo.xml. The
// answer is result 3 (CraftJackpot) and the reward page's jackpot flag. Mode 9 is the dialog's debug jackpot receive.
// The item ids are made up.
[NonParallelizable]
public class MaidCraftJackpotTests {
    private const int RecipeId = 4101;
    private const int Product = 20000101;
    private const int Jackpot = 20000104;
    private const int MaidCube = 50400001;
    private long MaidUid;
    private Plot plot = null!;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Login(float rate = 0.03f, int jackpotItem = Jackpot, bool registerJackpot = true) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Product));
        if (registerJackpot) {
            test.Register(TestGameSession.Metadata(Jackpot));
        }
        test.Register(TestGameSession.Metadata(MaidCube));
        test.Start();
        plot = test.EnterHome();
        test.SetTable(new MaidTable(new Dictionary<int, MaidTable.Entry>(), new Dictionary<int, int>(), new Dictionary<int, MaidTable.Recipe> {
            [RecipeId] = new MaidTable.Recipe(RecipeId, [], 0, [300, 200, 100], 50, 5, Product, 2, 1, new MaidTable.Jackpot(jackpotItem, 3, 2, rate)),
        }, new Dictionary<int, IReadOnlyList<MaidTable.RecipeLevel>> {
            [3] = [new MaidTable.RecipeLevel(RecipeId, 1)],
        }, new Dictionary<int, float> { [1] = 2f }));
        test.TakeSent();
    }

    // A maid of the session's account placed in its home with its craft stored.
    private void Place(long start, int duration) {
        Maid maid;
        using (GameStorage.Request db = test.Storage.Context()) {
            session.ItemMetadata.TryGet(MaidCube, out ItemMetadata? cubeMetadata);
            PlotCube? cube = db.CreateCube(plot, new PlotCube(cubeMetadata!) { Type = PlotCube.CubeType.Construction });
            Assert.That(cube, Is.Not.Null);
            MaidUid = cube!.Id;
            maid = db.CreateMaid(new Maid {
                Uid = MaidUid, OwnerId = session.AccountId, ItemId = MaidCube, MaidId = 3, CreationTime = 1700000000,
                ExpiryTime = DateTime.UtcNow.ToEpochSeconds() + 86400, Profile = new MaidProfile { Name = "n" },
            })!;
        }
        FieldManager field = session.Field!;
        Assert.That(field.SetMaidCraft(maid, RecipeId, start, duration), Is.True);
        Type slotType = typeof(FieldManager).GetNestedType("MaidSlot", BindingFlags.NonPublic)!;
        object slot = Activator.CreateInstance(slotType, maid)!;
        object maids = typeof(FieldManager).GetField("maids", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(field)!;
        maids.GetType().GetMethod("TryAdd")!.Invoke(maids, [maid.Uid, slot]);
        test.TakeSent();
    }

    private List<byte[]> Send(byte mode, double roll) {
        new MaidCraftItemHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata, Roll = () => roll }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(mode);
                writer.WriteLong(MaidUid);
            }));
        test.RunFieldQueue();
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MaidCraftItem).ToList();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter writer) => Convert.ToHexString(writer.ToArray());

    private int Held(int itemId) => session.Item.Inventory.Filter(item => item.Id == itemId).Sum(item => item.Amount);

    private (int RecipeId, long StartTime, int Duration)? Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMaidCraft(MaidUid);
    }

    // The reward packet's jackpot byte: the second to last (the merat flag is last).
    private static byte JackpotFlag(byte[] reward) => reward[^2];

    [Test]
    public void ARollUnderTheChanceGivesTheJackpot() {
        Login();
        Place(DateTime.UtcNow.ToEpochSeconds() - 5000, 300);

        // 0.03 times the grade 1 rate 2 (the test cube's rarity is 1): a roll of 0.059 is a jackpot.
        List<byte[]> sent = Send(6, 0.059);

        Assert.Multiple(() => {
            Assert.That(Held(Jackpot), Is.EqualTo(3));
            Assert.That(Held(Product), Is.EqualTo(0));
            Assert.That(Stored(), Is.EqualTo((0, 0L, 0)));
            Assert.That(sent.Select(Convert.ToHexString).Take(2), Is.EqualTo(new[] {
                Hex(MaidCraftItemPacket.Remove(MaidUid)),
                Hex(MaidCraftItemPacket.ReceiveResult(MaidRules.CraftJackpot)),
            }));
            Assert.That(sent, Has.Count.EqualTo(3));
            Assert.That(BitConverter.ToInt32(sent[2], 3), Is.EqualTo(Jackpot));
            Assert.That(JackpotFlag(sent[2]), Is.EqualTo(1));
        });
    }

    [Test]
    public void ARollAtTheChanceGivesTheProduct() {
        Login();
        Place(DateTime.UtcNow.ToEpochSeconds() - 5000, 300);

        List<byte[]> sent = Send(6, 0.06);

        Assert.Multiple(() => {
            Assert.That(Held(Product), Is.EqualTo(2));
            Assert.That(Held(Jackpot), Is.EqualTo(0));
            Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(MaidCraftItemPacket.ReceiveResult(MaidRules.CraftReceived))));
            Assert.That(JackpotFlag(sent[2]), Is.EqualTo(0));
        });
    }

    [Test]
    public void ARateOfZeroNeverGivesTheJackpot() {
        Login(rate: 0);
        Place(DateTime.UtcNow.ToEpochSeconds() - 5000, 300);

        Send(6, 0);

        Assert.Multiple(() => {
            Assert.That(Held(Product), Is.EqualTo(2));
            Assert.That(Held(Jackpot), Is.EqualTo(0));
        });
    }

    // A jackpot item missing from the item data gives the product, answered as a plain receive.
    [Test]
    public void AJackpotItemTheServerCannotMakeGivesTheProduct() {
        Login(registerJackpot: false);
        Place(DateTime.UtcNow.ToEpochSeconds() - 5000, 300);

        List<byte[]> sent = Send(6, 0);

        Assert.Multiple(() => {
            Assert.That(Held(Product), Is.EqualTo(2));
            Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(MaidCraftItemPacket.ReceiveResult(MaidRules.CraftReceived))));
        });
    }

    [Test]
    public void AMeretReceiveRollsToo() {
        Login();
        Place(DateTime.UtcNow.ToEpochSeconds() - 5000, 300);

        List<byte[]> sent = Send(7, 0);

        Assert.Multiple(() => {
            Assert.That(Held(Jackpot), Is.EqualTo(3));
            Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(MaidCraftItemPacket.MeretReceiveResult(MaidRules.CraftJackpot))));
            Assert.That(JackpotFlag(sent[2]), Is.EqualTo(1));
        });
    }

    [Test]
    public void TheDebugJackpotReceiveNeedsDebug() {
        Login();
        long start = DateTime.UtcNow.ToEpochSeconds();
        Place(start, 3700);

        List<byte[]> sent = Send(9, 0.99);

        Assert.Multiple(() => {
            Assert.That(sent, Is.Empty);
            Assert.That(Held(Jackpot), Is.EqualTo(0));
            Assert.That(Stored(), Is.EqualTo((RecipeId, start, 3700)));
        });
    }

    // Mode 9 with Debug: the jackpot whatever the roll, of a craft not ready yet, answered with MaidCraftItem 9.
    [Test]
    public void TheDebugJackpotReceiveGivesTheJackpotAtOnce() {
        Login();
        Place(DateTime.UtcNow.ToEpochSeconds(), 3700);
        session.Player.AdminPermissions = AdminPermissions.Debug;

        List<byte[]> sent = Send(9, 0.99);

        Assert.Multiple(() => {
            Assert.That(Held(Jackpot), Is.EqualTo(3));
            Assert.That(Stored(), Is.EqualTo((0, 0L, 0)));
            Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(MaidCraftItemPacket.JackpotReceiveResult(MaidRules.CraftJackpot))));
            Assert.That(JackpotFlag(sent[2]), Is.EqualTo(1));
        });
    }

    [Test]
    public void JackpotRowNeedsAnItemAndKeepsItsValuesInRange() {
        static Func<string, string> Attrs(params (string Key, string Value)[] values) =>
            key => values.FirstOrDefault(entry => entry.Key == key).Value ?? "";

        MaidTable.Jackpot? row = MaidTable.JackpotRow(Attrs(("JackpotItemID", "40100002"), ("JackpotCount", "2"), ("JackpotRank", "3"), ("JackpotRate", "0.03")));
        Assert.Multiple(() => {
            Assert.That(row, Is.EqualTo(new MaidTable.Jackpot(40100002, 2, 3, 0.03f)));
            Assert.That(MaidTable.JackpotRow(Attrs(("JackpotItemID", ""), ("JackpotRate", "0.03"))), Is.Null);
            Assert.That(MaidTable.JackpotRow(Attrs(("JackpotItemID", "0"))), Is.Null);
            Assert.That(MaidTable.JackpotRow(Attrs(("JackpotItemID", "5"), ("JackpotRank", "9"), ("JackpotRate", "-1"))),
                Is.EqualTo(new MaidTable.Jackpot(5, 1, 6, 0)));
            Assert.That(MaidTable.GradeRow("6", "3"), Is.EqualTo((6, 3f)));
            Assert.That(MaidTable.GradeRow("1", "1.01"), Is.EqualTo((1, 1.01f)));
            Assert.That(MaidTable.GradeRow("x", "1"), Is.Null);
            Assert.That(MaidTable.GradeRow("1", "-1"), Is.Null);
        });
    }

    [Test]
    public void TheChanceIsTheRecipeRateTimesTheGradeRate() {
        var recipe = new MaidTable.Recipe(1, [], 0, [1, 1, 1], 0, 0, 2, 1, 1, new MaidTable.Jackpot(3, 1, 1, 0.04f));
        var rates = new Dictionary<int, float> { [0] = 1f, [6] = 3f, [5] = 100f };
        Assert.Multiple(() => {
            Assert.That(MaidRules.JackpotChance(recipe, rates, 6), Is.EqualTo(0.12).Within(1e-6));
            Assert.That(MaidRules.JackpotChance(recipe, rates, 4), Is.EqualTo(0.04).Within(1e-6)); // unlisted grade: 1
            Assert.That(MaidRules.JackpotChance(recipe, null, 6), Is.EqualTo(0.04).Within(1e-6));
            Assert.That(MaidRules.JackpotChance(recipe, rates, 5), Is.EqualTo(1));
            Assert.That(MaidRules.JackpotChance(recipe with { Jackpot = null }, rates, 6), Is.EqualTo(0));
        });
    }

    // Cancel (mode 10, the dialog's onRecieveOrCancel while the craft runs, after message 0x7b7 is confirmed): the craft
    // record goes (Remove) and the answer is result 4, which the dialog takes as cancelled (OnCancelResult redraws the
    // progress); no product is given. A maid without a craft gets no answer.
    [Test]
    public void Cancel_RunningCraft_EndsItWithoutTheProduct() {
        Login();
        Place(DateTime.UtcNow.ToEpochSeconds() - 10, 300);

        List<byte[]> sent = Send(10, 0.5);

        Assert.Multiple(() => {
            Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
                Hex(MaidCraftItemPacket.Remove(MaidUid)),
                Hex(MaidCraftItemPacket.CancelResult(MaidRules.CraftCancelled)),
            }));
            Assert.That(Stored(), Is.EqualTo((0, 0L, 0)));
            Assert.That(Held(Product), Is.EqualTo(0));
        });
        Assert.That(Send(10, 0.5), Is.Empty);
    }

    // A receive (mode 6) before the craft's ready time is answered CraftNotReady and keeps the craft.
    [Test]
    public void Receive_BeforeReady_KeepsTheCraft() {
        Login();
        long start = DateTime.UtcNow.ToEpochSeconds() - 10;
        Place(start, 300);

        List<byte[]> sent = Send(6, 0.5);

        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(MaidCraftItemPacket.ReceiveResult(MaidRules.CraftNotReady)) }));
        Assert.That(Stored()?.RecipeId, Is.EqualTo(RecipeId));
        Assert.That(Held(Product), Is.EqualTo(0));
    }
}
