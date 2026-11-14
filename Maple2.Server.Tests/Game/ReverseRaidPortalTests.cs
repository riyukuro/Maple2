using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;

namespace Maple2.Server.Tests.Game;

// The reverse raid hall's ReverseRaidPortal widget (63000054): the event row reader, the open portal rule, and the
// widget that shows the hall's raid portals while a ReverseRaidDungeonOpen event names them.
public class ReverseRaidPortalTests : TriggerTestBase {
    private const int Zakum = 20007001;
    private const int PinkBean = 20023001;

    private static DungeonGroupType? GroupOf(int roomId) => roomId == Zakum ? DungeonGroupType.reverseRaid : roomId == 20003001 ? DungeonGroupType.normal : null;

    [Test]
    public void Reader_TakesThePortalIds() {
        Assert.That(EventXmlReader.ReverseRaidDungeonOpen("20007001,20023001")!.PortalIds, Is.EqualTo(new[] { Zakum, PinkBean }));
        Assert.That(EventXmlReader.ReverseRaidDungeonOpen(" 20007001 , x,0,20007001,")!.PortalIds, Is.EqualTo(new[] { Zakum }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("x,0,-3")]
    public void Reader_NoPortal_IsNoEvent(string? value1) {
        Assert.That(EventXmlReader.ReverseRaidDungeonOpen(value1), Is.Null);
    }

    [Test]
    public void Replay_TakesTheEvent() {
        Assert.That(AttendGiftReplay.Types, Does.Contain(GameEventType.ReverseRaidDungeonOpen));
        Assert.That(AttendGiftReplay.NeedData, Does.Contain(GameEventType.ReverseRaidDungeonOpen));
    }

    // The client has no GameEventReverseRaidDungeonOpen class and drops the rest of the event list at an unknown
    // name, so the event is kept off the list.
    [Test]
    public void Event_NotSentToClient() {
        var row = new GameEventMetadata(360007, GameEventType.ReverseRaidDungeonOpen, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero,
            TimeSpan.Zero, [], new ReverseRaidDungeonOpen([Zakum, PinkBean]), "", "", "", "");
        Assert.That(new GameEvent(row).SentToClient, Is.False);
    }

    // Only reverse raid rooms the server has are opened: KR has no room 20023001, and a normal room is not one.
    [Test]
    public void Rules_OpenOnlyReverseRaidRooms() {
        GameEventData?[] events = [new ReverseRaidDungeonOpen([Zakum, PinkBean, 20003001]), null];
        Assert.That(ReverseRaidPortalRules.OpenPortals(events, GroupOf), Is.EquivalentTo(new[] { Zakum }));
        Assert.That(ReverseRaidPortalRules.OpenPortals([], GroupOf), Is.Empty);
    }

    private FieldPortal hallPortal = null!;

    private ReverseRaidPortalWidget StartHall() {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Zakum, 2000023) with { GroupType = DungeonGroupType.reverseRaid };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Zakum] = room }));
        test.Start();
        SetTriggers();
        context = NewContext();
        // The test field skips the FieldManager ctor, so its portal and widget tables are made here.
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(Field, new Dictionary<string, Widget>());
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, Activator.CreateInstance(portals.FieldType));
        // As the hall's trigger leaves it: hidden, disabled, the minimap icon on.
        hallPortal = Field.SpawnPortal(new Portal(Zakum, 0, 0, PortalType.Boss, PortalActionType.Interact, Vector3.Zero, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, false, true, false));
        Action("""<action name="create_widget" type="ReverseRaidPortal" />""");
        test.TakeSent();
        return (ReverseRaidPortalWidget) Field.Widgets["ReverseRaidPortal"];
    }

    [Test]
    public void Widget_EventOpensThePortal_ThenClosesIt() {
        ReverseRaidPortalWidget widget = StartHall();

        widget.Refresh([new ReverseRaidDungeonOpen([Zakum, PinkBean])], null);
        Assert.That((hallPortal.Visible, hallPortal.Enabled, hallPortal.MinimapVisible), Is.EqualTo((true, true, true)));
        ByteReader reader = Sent(SendOp.FieldPortal).Single();
        Assert.That((reader.ReadByte(), reader.ReadInt(), reader.ReadBool(), reader.ReadBool(), reader.ReadBool()), Is.EqualTo(((byte) 2, Zakum, true, true, true)));

        // The same events again: nothing changes, nothing is sent.
        widget.Refresh([new ReverseRaidDungeonOpen([Zakum])], null);
        Assert.That(Sent(SendOp.FieldPortal), Is.Empty);

        // The event is over: the portal is hidden again.
        widget.Refresh([], null);
        Assert.That((hallPortal.Visible, hallPortal.Enabled), Is.EqualTo((false, false)));
        Assert.That(Sent(SendOp.FieldPortal), Has.Count.EqualTo(1));
    }

    // The entering player is sent the field's portals right after, so the change is not sent to them.
    [Test]
    public void Widget_SkipsTheEnteringPlayer() {
        ReverseRaidPortalWidget widget = StartHall();
        widget.Refresh([new ReverseRaidDungeonOpen([Zakum])], test.Session);
        Assert.That(hallPortal.Visible, Is.True);
        Assert.That(Sent(SendOp.FieldPortal), Is.Empty);
    }

    [Test]
    public void Widget_NoEvent_LeavesTheScriptsPortal() {
        ReverseRaidPortalWidget widget = StartHall();
        widget.Refresh([], null);
        Assert.That((hallPortal.Visible, hallPortal.Enabled, hallPortal.MinimapVisible), Is.EqualTo((false, false, true)));
        Assert.That(Sent(SendOp.FieldPortal), Is.Empty);
    }
}
