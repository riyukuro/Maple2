using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// SkillBookTree 0x10C (UISkillBookTreeDialog::vfunc_79, uiskillbookautodistributedialog.cpp:1962): byte mode;
// 0: int max tabs, long active tab, int count x tab (CSkillBookTreeTabConfig::Read @0x14026a860: long id, unicode
// name, int count x (int skill, int points)); 1: long active tab, long saved tab, int rank; 2: long tab, unicode name,
// byte result; 4: int max tabs.
// SkillPoint 0x44 (CSceneStateGame::OnSkillPoint, scenestategame.cpp:4241): int total, int count x (int source,
// int count x (short rank, int points)), int score.
public class SkillBookLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void LoadIsTabsWithSkills() {
        var tab = new SkillTab("tab") { Id = 7 };
        tab.Skills[10000001] = 3;
        var book = new SkillBook { MaxSkillTabs = 2, ActiveSkillTabId = 7, SkillTabs = [tab] };
        ByteReader reader = Read(SkillBookPacket.Load(book), SendOp.SkillBookTree);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("tab"));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(10000001));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SaveRenameExpand() {
        var book = new SkillBook { MaxSkillTabs = 3, ActiveSkillTabId = 7 };
        ByteReader reader = Read(SkillBookPacket.Save(book, 8, 2), SendOp.SkillBookTree);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadLong(), Is.EqualTo(8));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(SkillBookPacket.Rename(new SkillTab("x") { Id = 8 }, error: true), SendOp.SkillBookTree);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(8));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("x"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(SkillBookPacket.Expand(book), SendOp.SkillBookTree);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SkillPointSourcesAreIntKeyed() {
        var points = new SkillPoint();
        points[SkillPointSource.Chapter][1] = 4;
        ByteReader reader = Read(SkillPointPacket.Sources(points), SendOp.SkillPoint);
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        int sources = reader.ReadInt();
        Assert.That(sources, Is.EqualTo(3));
        int awakening = 0;
        for (int i = 0; i < sources; i++) {
            int source = reader.ReadInt();
            int ranks = reader.ReadInt();
            for (int j = 0; j < ranks; j++) {
                short rank = reader.ReadShort();
                int value = reader.ReadInt();
                if (source == (int) SkillPointSource.Chapter && rank == 2) {
                    awakening = value;
                }
            }
        }
        Assert.That(awakening, Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
