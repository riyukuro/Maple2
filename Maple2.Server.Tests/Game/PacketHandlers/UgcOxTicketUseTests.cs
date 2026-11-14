using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The OX quiz host ticket (KR 20300990, item function OpenUGCEvent "ox"): the client sends a plain item use. The server
// opens the create dialog (HomeAction s2c fees) in the user's own home and refuses elsewhere. The ticket stays: it is
// taken when the event is made.
public class UgcOxTicketUseTests {
    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item ticket = null!;

    private GameSession Start() {
        test = new TestGameSession();
        ticket = test.Store(TestGameSession.Metadata(UgcOxRules.TicketItemId,
            function: new ItemMetadataFunction(ItemFunction.OpenUGCEvent, "OpenUGCEvent", "ox", false), type: 10));
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        // A field factory with no OX event running (the test session has none).
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        typeof(FieldManager.Factory).GetField("ugcOxEvents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, new ConcurrentDictionary<long, UgcOxEvent>());
        typeof(GameSession).GetField("<FieldFactory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, factory);
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(ticket.Uid);
        writer.WriteUnicodeString("");
    }));

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    [Test]
    public void Use_InOwnHome_OpensTheCreateDialog() {
        GameSession session = Start();
        test.EnterHome();

        Use(session);

        Assert.Multiple(() => {
            Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(HomeActionPacket.UgcOxFees(UgcOxRules.Fees).ToArray()) }));
            Assert.That(session.Item.Inventory.Get(ticket.Uid)?.Amount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Use_OutsideAHome_Refuses() {
        GameSession session = Start();

        Use(session);

        Assert.Multiple(() => {
            Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(NoticePacket.Message("s_ugcox_create_fail_other_home").ToArray()) }));
            Assert.That(session.Item.Inventory.Get(ticket.Uid)?.Amount, Is.EqualTo(1));
        });
    }
}
