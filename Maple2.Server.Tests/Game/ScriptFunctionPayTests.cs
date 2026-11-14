using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A script function's price (collectItem*, collectMeso) is taken before any of it runs: a talker who cannot pay gets
// no present, exp or warp. The client runs no function step (functionID is only stored, scriptcontent.cpp), so the
// server is the only check. A collected 90000001-90000003 is mesos (KR npc 11000600 asks 10000 of 90000001).
// The npc, script and item ids are made up.
public class ScriptFunctionPayTests {
    private const int NpcId = 1000;
    private const int Material = 30000193;
    private const int Present = 30000145;

    private TestGameSession test = null!;

    private static ScriptFunctionMetadata Function(IList<ItemComponent> collect, long collectMeso = 0, long presentExp = 0) =>
        new(NpcId, 21, ScriptType.Npc, 1, false, 0, "", "", "", 0, 0, "", "",
            [new ItemComponent(Present, 1, 1, ItemTag.None)], collect, 0, "", "", "", presentExp, collectMeso, 0, 0, false);

    // Runs the function of talk 21 after the talker got `material` of the material and `meso` mesos.
    private GameSession Run(ScriptFunctionMetadata function, int material, long meso) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Present));
        if (material > 0) {
            test.Store(TestGameSession.Metadata(Material), material);
        }
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> {
            [NpcId] = new() { [21] = new() { [1] = function } },
        }));
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        var content = new CinematicContent("", NpcTalkButton.Close, 1, [], []);
        var state = new ScriptState(21, ScriptStateType.Script, false, null, [content]);
        var script = new ScriptMetadata(NpcId, ScriptType.Npc, new Dictionary<int, ScriptState> { [21] = state });
        session.NpcScript = new NpcScriptManager(session, null, script, state, NpcTalkType.Talk);
        test.TakeSent();
        session.NpcScript.ProcessScriptFunction();
        return session;
    }

    private static int Count(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    [Test]
    public void MissingItems_GiveNothing() {
        GameSession session = Run(Function([new ItemComponent(Material, -1, 10, ItemTag.None)], presentExp: 10000), material: 9, meso: 0);
        Assert.Multiple(() => {
            Assert.That(Count(session, Material), Is.EqualTo(9));
            Assert.That(Count(session, Present), Is.EqualTo(0));
            Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(0));
        });
    }

    [Test]
    public void TheItems_AreTakenForThePresent() {
        GameSession session = Run(Function([new ItemComponent(Material, -1, 10, ItemTag.None)]), material: 12, meso: 0);
        Assert.Multiple(() => {
            Assert.That(Count(session, Material), Is.EqualTo(2));
            Assert.That(Count(session, Present), Is.EqualTo(1));
        });
    }

    // 90000001 x10000 is 10000 mesos, not a bag item.
    [Test]
    public void ACollectedMesoItem_TakesMesos() {
        GameSession paid = Run(Function([new ItemComponent(90000001, -1, 10000, ItemTag.None)]), material: 0, meso: 15000);
        Assert.Multiple(() => {
            Assert.That(paid.Currency.Meso, Is.EqualTo(5000));
            Assert.That(Count(paid, Present), Is.EqualTo(1));
        });
        GameSession poor = Run(Function([new ItemComponent(90000001, -1, 10000, ItemTag.None)]), material: 0, meso: 9999);
        Assert.Multiple(() => {
            Assert.That(poor.Currency.Meso, Is.EqualTo(9999));
            Assert.That(Count(poor, Present), Is.EqualTo(0));
        });
    }

    // Mesos and items asked together: short items give the mesos back.
    [Test]
    public void ShortItems_KeepTheMesos() {
        GameSession session = Run(Function([new ItemComponent(Material, -1, 10, ItemTag.None)], collectMeso: 1000), material: 3, meso: 1500);
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(1500));
            Assert.That(Count(session, Material), Is.EqualTo(3));
            Assert.That(Count(session, Present), Is.EqualTo(0));
        });
    }

    // presentExp goes through the exp manager (the client is sent the gain), not straight onto the character.
    [Test]
    public void PresentExp_IsAGain() {
        GameSession session = Run(Function([new ItemComponent(Material, -1, 1, ItemTag.None)], presentExp: 100), material: 1, meso: 0);
        Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(100));
    }
}
