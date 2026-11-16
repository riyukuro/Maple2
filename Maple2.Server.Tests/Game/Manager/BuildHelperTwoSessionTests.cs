using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Two sessions in one home: who may build in whose home. A helper may build only while the owner is in the field and
// lists the helper's account; the owner's own listing grants a helper in the field at once (0x35) and removing it takes
// the grant back; a helper that enters a listed home is granted, one not listed is not; the owner leaving takes the
// grant back, and a planner mode of the home takes the build right away.
public class BuildHelperTwoSessionTests {
    private TestGameSession ownerTest = null!;
    private TestGameSession helperTest = null!;
    private GameSession owner => ownerTest.Session;
    private GameSession helper => helperTest.Session;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        ownerTest = new TestGameSession();
        ownerTest.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        ownerTest.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        ownerTest.Start();
        owner.Housing.InitNewHome("home", null);
        owner.Player.Value.Home.BuildMeso = 3000;
        // The home field is the owner's: its owner is the home's account.
        typeof(Home).GetField("<AccountId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(owner.Player.Value.Home, owner.AccountId);
        plot = ownerTest.EnterHome();

        helperTest = new TestGameSession(ownerTest.Options);
        helperTest.Start();
        helper.Field = owner.Field;
        owner.Field!.Players.Clear(); // nobody is in the field until a test brings them in
        // The housing trophies are read with a MySQL query: the lists are given.
        foreach (GameSession session in new[] { owner, helper }) {
            typeof(ItemMetadataStorage).GetField("housingTrophies", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session.ItemMetadata, new List<(int, int)>());
        }
        ownerTest.World.AddPlayer(helper.AccountId, helper.CharacterId, helper.PlayerName);
        ownerTest.TakeSent();
        helperTest.TakeSent();
    }

    // The test players share an object id, so each gets a key of its own in the field's player list.
    private static int Key(GameSession session) => (int) (session.AccountId % 100000) + 1;
    private void Enter(GameSession session) => session.Field!.Players[Key(session)] = session.Player;

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private string[] SentTo(TestGameSession test) => test.TakeSent().Select(Convert.ToHexString).ToArray();

    [Test]
    public void ANeverListedHelper_MayNotBuild() {
        Enter(owner);
        Enter(helper);

        Assert.That(helper.Housing.GetBuildFieldPlot(out _), Is.Null);
    }

    [Test]
    public void AListedHelper_BuildsOnTheOwnersPlot_WithTheOwnersHousing() {
        Enter(owner);
        Enter(helper);
        owner.Player.Value.Home.Builders.Add(helper.AccountId);

        Plot? found = helper.Housing.GetBuildFieldPlot(out GameSession host);

        Assert.That(found, Is.SameAs(plot));
        Assert.That(host, Is.SameAs(owner));
    }

    [Test]
    public void AListedHelper_MayNotBuild_WhileTheOwnerIsNotInTheField() {
        Enter(helper);
        owner.Player.Value.Home.Builders.Add(helper.AccountId);

        Assert.That(helper.Housing.GetBuildFieldPlot(out _), Is.Null);
    }

    [Test]
    public void AListedHelper_MayNotBuild_WhileTheHomeIsInAPlannerMode() {
        Enter(owner);
        Enter(helper);
        owner.Player.Value.Home.Builders.Add(helper.AccountId);
        plot.SetPlannerMode(Maple2.Model.Enum.PlotMode.DecorPlanner);

        Assert.That(helper.Housing.GetBuildFieldPlot(out _), Is.Null);
    }

    [Test]
    public void ListingAHelperInTheField_GrantsItAtOnce_AndRemovingTakesTheGrantBack() {
        Enter(owner);
        Enter(helper);

        owner.Housing.AddBuilder(helper.PlayerName);

        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(owner.AccountId));
        Assert.That(SentTo(helperTest), Does.Contain(Hex(CubePacket.GrantBuildPermissions(helper.AccountId, owner.AccountId, 3000, 0))));

        owner.Housing.RemoveBuilder(helper.PlayerName);

        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(0));
        Assert.That(SentTo(helperTest), Does.Contain(Hex(CubePacket.GrantBuildPermissions(helper.AccountId))));
        Assert.That(owner.Housing.IsBuilder(helper.AccountId), Is.False);
    }

    [Test]
    public void AHelperEnteringAListedHome_IsGranted_ANotListedOneIsNot() {
        Enter(owner);
        Enter(helper);

        helper.Housing.EnterBuildField();
        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(0));

        owner.Player.Value.Home.Builders.Add(helper.AccountId);
        helper.Housing.EnterBuildField();
        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(owner.AccountId));
    }

    [Test]
    public void TheOwnerEntering_GetsItsListAndBudget_AndTellsTheHelpersInTheField() {
        owner.Player.Value.Home.Builders.Add(helper.AccountId);
        Enter(owner);
        Enter(helper);

        owner.Housing.EnterBuildField();

        string[] toOwner = SentTo(ownerTest);
        Assert.That(toOwner, Does.Contain(Hex(CubePacket.UpdateBuildBudget(0, 3000, 0))));
        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(owner.AccountId));
    }

    [Test]
    public void TheOwnerLeaving_TakesTheGrantBackFromTheHelpers() {
        Enter(owner);
        Enter(helper);
        owner.Player.Value.Home.Builders.Add(helper.AccountId);
        owner.Housing.EnterBuildField();
        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(owner.AccountId));

        owner.Housing.LeaveBuildField();

        Assert.That(helper.Housing.BuildOwnerId, Is.EqualTo(0));
    }
}
