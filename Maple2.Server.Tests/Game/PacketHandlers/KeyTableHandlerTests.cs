using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// KeyTable 0x3A modes the client sends outside the full key upload (mode 2). CKeyTable::SendKeyTableMode1
// (client/ms2/keytable.cpp:385): byte 1, int 1, then the one bind (int key, int type, long code, int slot, byte
// priority). CKeyTable's skill set exit (keytable.cpp:2031): SendKeysToServer (mode 2) and then byte 7 alone.
public class KeyTableHandlerTests {
    private TestGameSession test = null!;

    [SetUp]
    public void Login() {
        test = new TestGameSession();
        test.Start();
        test.SavingConfig(test.Session);
        test.TakeSent();
    }

    private Dictionary<int, KeyBind> Stored() =>
        (Dictionary<int, KeyBind>) typeof(ConfigManager).GetField("keyBinds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.Session.Config)!;

    private void Send(byte mode, params KeyBind[] binds) {
        new KeyTableHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            if (mode is 1 or 2) {
                writer.WriteInt(binds.Length);
                foreach (KeyBind bind in binds) {
                    writer.WriteInt(bind.KeyCode);
                    writer.WriteInt(bind.OptionType);
                    writer.WriteLong(bind.OptionGuid);
                    writer.WriteInt(bind.Unknown1);
                    writer.WriteByte(bind.Priority);
                }
            }
        }));
    }

    [Test]
    public void Mode1_StoresTheOneBind() {
        var bind = new KeyBind(0x41, 2, 300001, 0, 0);

        Send(1, bind);

        Assert.That(Stored(), Does.ContainKey(0x41));
        Assert.That(Stored()[0x41], Is.EqualTo(bind));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Mode1_ReplacesTheKeysBind() {
        Send(1, new KeyBind(0x41, 2, 300001, 0, 0));
        var replaced = new KeyBind(0x41, 5, 10000001, -1, 1);

        Send(1, replaced);

        Assert.That(Stored()[0x41], Is.EqualTo(replaced));
        Assert.That(Stored(), Has.Count.EqualTo(1));
    }

    // The keys came with mode 2 just before; mode 7 changes none and is not answered.
    [Test]
    public void Mode7_AfterTheUpload_ChangesNothing() {
        var bind = new KeyBind(0x41, 2, 300001, 0, 0);
        Send(2, bind);

        Send(7);

        Assert.That(Stored(), Is.EqualTo(new Dictionary<int, KeyBind> { [0x41] = bind }));
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
