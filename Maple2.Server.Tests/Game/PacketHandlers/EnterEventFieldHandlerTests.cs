using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// EnterEventField (the event field popup's move button), on the KR row <gameEvent gameEventID="150017"
// eventType="EventFieldPopup" value1="63000060" />: the running popup's field is entered; no popup, a dead player,
// the popup's own field or a player in battle is answered with its notice and stays.
public class EnterEventFieldHandlerTests {
    private const int From = 2000062;
    private const int EventField = 63000060;
    private TestGameSession test = null!;

    private static MapMetadata Field(int id) => TestGameSession.Map(id) with {
        Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
    };

    private void Start(bool popup = true, int fieldId = From) {
        test = new TestGameSession();
        if (popup) {
            test.AddEvent(150017, GameEventType.EventFieldPopup, new EventFieldPopup(EventField));
        }
        test.Start();
        MapMetadata from = Field(fieldId);
        test.AddMap(fieldId == EventField ? [from] : [from, Field(EventField)]);
        test.EnterMovingOut(from);
        test.TakeSent();
    }

    private List<string> Enter() {
        new EnterEventFieldHandler { GameStorage = test.Storage }.Handle(test.Session, TestGameSession.Packet(_ => { }));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static string Alert(StringCode code) => Convert.ToHexString(ChatPacket.Alert(code).ToArray());

    [Test]
    public void RunningPopup_EntersItsField() {
        Start();

        Enter();

        Assert.That(test.World.Moves.Select(move => move.MapId), Is.EqualTo(new[] { EventField }));
    }

    [Test]
    public void NoPopup_AnswersMoveField() {
        Start(popup: false);

        Assert.That(Enter(), Is.EqualTo(new[] { Alert(StringCode.s_err_timeevent_move_field) }));
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void InThePopupsField_AnswersSameField() {
        Start(fieldId: EventField);

        Assert.That(Enter(), Is.EqualTo(new[] { Alert(StringCode.s_err_timeevent_samefield) }));
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void InBattle_AnswersBattle() {
        Start();
        test.Session.Player.InBattle = true;
        test.TakeSent();

        Assert.That(Enter(), Is.EqualTo(new[] { Alert(StringCode.s_err_timeevent_battle) }));
        Assert.That(test.World.Moves, Is.Empty);
    }
}
