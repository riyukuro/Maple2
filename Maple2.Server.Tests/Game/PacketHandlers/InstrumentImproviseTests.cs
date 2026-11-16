using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The improvisation and ensemble modes of PlayInstrument: a note is sent only while improvising, the stop ends the
// play and tells the field, a score stop does nothing for an improvisation (it gives no mastery), and leaving an
// ensemble answers once (Instrument 6) so the client leaves its wait state.
public class InstrumentImproviseTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item item = null!;

    private void Begin() {
        test = new TestGameSession();
        ItemMetadata instrument = TestGameSession.Metadata(34000001,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", "6", false), type: 6);
        test.Register(instrument);
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> { [6] = new(6, 0, 3, 1, 0, 0) }));
        item = test.Store(instrument);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
    }

    private void Send(byte mode, System.Action<IByteWriter>? body = null) {
        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(mode);
                body?.Invoke(writer);
            }));
    }

    [Test]
    public void ANoteWithoutAPlay_IsIgnored() {
        Begin();
        Send(1, writer => writer.WriteInt(60));
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Instrument, Is.Null);
    }

    [Test]
    public void AStartWhilePlaying_IsIgnored() {
        Begin();
        Send(0, writer => writer.WriteLong(item.Uid));
        Assert.That(session.Instrument?.Improvising, Is.True);
        var first = session.Instrument!;
        test.TakeSent();

        Send(0, writer => writer.WriteLong(item.Uid));
        Assert.That(session.Instrument, Is.SameAs(first));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AStopOfAnImprovisation_EndsThePlayAndTellsTheField() {
        Begin();
        Send(0, writer => writer.WriteLong(item.Uid));
        test.TakeSent();

        Send(2);
        Assert.That(session.Instrument, Is.Null);
        Assert.That(test.TakeSent(), Is.Not.Empty);
    }

    [Test]
    public void AScoreStopDuringAnImprovisation_GivesNothing() {
        Begin();
        Send(0, writer => writer.WriteLong(item.Uid));
        test.TakeSent();

        Send(4);
        Assert.That(session.Instrument?.Improvising, Is.True);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void LeavingAnEnsemble_AnswersOnceAndClearsTheStaging() {
        Begin();
        Send(6);
        Assert.That(session.EnsembleReady, Is.False);
        Assert.That(session.StagedScoreItem, Is.Null);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

}
