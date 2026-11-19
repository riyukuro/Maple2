using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The skill macro dialog's packet (RecvOp.SkillMacro): a save is stored only when every tab passes what the client's
// own dialog keeps to, and the load answers with the stored tabs.
public class SkillMacroHandlerTests {
    private TestGameSession test = null!;
    private SkillMacroHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        // The login loads the stored tabs; a character without any has an empty list.
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("skillMacros", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(test.Session.Config, new List<SkillMacro>());
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(test.Session.Config, test.Session);
        handler = new SkillMacroHandler();
        test.TakeSent();
    }

    private void Update(params (string Name, long Key, int[] Skills)[] tabs) {
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(tabs.Length);
            foreach ((string name, long key, int[] skills) in tabs) {
                writer.WriteUnicodeString(name);
                writer.WriteLong(key);
                writer.WriteInt(skills.Length);
                foreach (int skill in skills) {
                    writer.WriteInt(skill);
                }
            }
        }));
    }

    private byte[] Load() {
        test.TakeSent();
        handler.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(0)));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.SkillMacro));
        return sent[0];
    }

    private static byte[] Expected(params SkillMacro[] macros) => SkillMacroPacket.Load(macros).ToArray();

    [Test]
    public void Update_StoresTheTabsAndLoadSendsThemBack() {
        Update(("a", 5, [11, 12, 30]), ("b", 6, []));

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11, 12, 30 }), new SkillMacro("b", 6, new HashSet<int>()))));
    }

    [Test]
    public void Load_BeforeAnySaveSendsAnEmptyList() {
        Assert.That(Load(), Is.EqualTo(Expected()));
    }

    // The dialog reads the answer only as mode 0 (UISkillCompactControlDialog::vfunc_79); mode 2 is the login's.
    [Test]
    public void Load_AnswersInMode0() {
        var reader = new ByteReader(Load(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillMacro));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.Zero);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void Update_EmptyTabListStoresNothing() {
        Update(("a", 5, [11]));
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(0);
        }));

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11 }))));
    }

    // A close right after the load answer (the client's list may still be empty) keeps the stored tabs.
    [Test]
    public void Update_EmptyTabListRightAfterTheLoadStoresNothing() {
        Update(("a", 5, [11]));
        Load();
        Update();

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11 }))));
    }

    // The player removed the last tab some time after the tabs arrived: the empty list is stored.
    [Test]
    public void Update_EmptyTabListAfterTheGraceIsStored() {
        Update(("a", 5, [11]));
        Load();
        test.Session.SkillMacroLoadTick -= SkillMacroRules.EmptySaveGraceMs;
        Update();

        Assert.That(Load(), Is.EqualTo(Expected()));
    }

    [Test]
    public void Update_RepeatedNameKeepsTheOldTabs() {
        Update(("a", 5, [11]));
        Update(("x", 1, []), ("X", 2, []));

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11 }))));
    }

    [Test]
    public void Update_SkillIdsMustRiseKeepsTheOldTabs() {
        Update(("a", 5, [11]));
        Update(("x", 1, [20, 20]));
        Update(("x", 1, [20, 10]));
        Update(("x", 1, [0]));

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11 }))));
    }

    [Test]
    public void Update_SecondTabNameOverFiveLettersKeepsTheOldTabs() {
        Update(("a", 5, [11]));
        Update(("first tab name is long", 1, []), ("sixsix", 2, []));

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11 }))));
    }

    [Test]
    public void Update_SkillCountBeyondThePacketKeepsTheOldTabs() {
        Update(("a", 5, [11]));
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(1);
            writer.WriteUnicodeString("x");
            writer.WriteLong(1);
            writer.WriteInt(1000);
        }));

        Assert.That(Load(), Is.EqualTo(Expected(new SkillMacro("a", 5, new HashSet<int> { 11 }))));
    }
}
