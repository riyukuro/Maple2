using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// SuperWorldChat select: the line's int is the item id (the client resolves it as item data, texttohtml
// FUN_14183eba0), not the item's first function parameter, which names the icon.
public class SuperChatHandlerTests {
    private const int ItemId = 20801012;

    private TestGameSession test = null!;
    private readonly SuperChatHandler handler = new();

    private GameSession Login(ItemFunction function) {
        test = new TestGameSession();
        test.Store(TestGameSession.Metadata(ItemId,
            function: new ItemMetadataFunction(function, function.ToString(), "20800026,3,ff66fa", false)));
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private void Select(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteByte(0);
        writer.WriteInt(ItemId);
    }));

    // The super chat tab lists the wedding megaphones too (GetWeddingChatItems, uisidepopuptalk_2.cpp:211).
    [TestCase(ItemFunction.SuperWorldChat)]
    [TestCase(ItemFunction.WeddingChat)]
    public void Select_KeepsTheItemId(ItemFunction function) {
        GameSession session = Login(function);

        Select(session);

        Assert.That(session.SuperChatId, Is.EqualTo(ItemId));
        Assert.That(session.SuperChatItemId, Is.EqualTo(ItemId));
    }

    [Test]
    public void Select_OfAnotherFunctionKeepsNothing() {
        GameSession session = Login(ItemFunction.ExpandInven);

        Select(session);

        Assert.That(session.SuperChatId, Is.EqualTo(0));
    }

    [Test]
    public void Deselect_Clears() {
        GameSession session = Login(ItemFunction.SuperWorldChat);
        Select(session);

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

        Assert.That(session.SuperChatId, Is.EqualTo(0));
        Assert.That(session.SuperChatItemId, Is.EqualTo(0));
    }
}
