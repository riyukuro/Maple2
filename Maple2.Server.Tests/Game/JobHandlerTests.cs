using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The Job packet (RecvOp.Job): what the skill tree dialog sends and what the client gets back.
public class JobHandlerTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private JobHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill))!.SetValue(session.Config, new SkillManager(session, new SkillBook()));
        typeof(ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
        BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ConfigManager).GetField("skillPoints", any)!.SetValue(session.Config, new SkillPoint());
        typeof(ConfigManager).GetField("hotBars", any)!.SetValue(session.Config, new List<HotBar>());
        handler = new JobHandler();
        test.TakeSent();
    }

    private void Send(byte command, System.Action<Maple2.PacketLib.Tools.ByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    private List<SendOp> Ops() => test.TakeSent().Select(TestGameSession.Op).ToList();

    [Test]
    public void Load_SendsTheSkillTree() {
        Send(8);

        Assert.That(Ops().Count(op => op == SendOp.Job), Is.EqualTo(1));
    }

    [Test]
    public void UnknownCommandSevenReadsItsFieldsAndSendsNothing() {
        Send(7, writer => {
            writer.WriteInt(1);
            writer.WriteLong(2);
            writer.WriteInt(3);
            writer.WriteShort(4);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Update_WithAnEmptyTreeStillRepliesWithTheStoredTree() {
        Send(9, writer => writer.WriteInt(0));

        Assert.That(Ops().Count(op => op == SendOp.Job), Is.EqualTo(1));
    }

    [Test]
    public void Update_OnAMapWithLimitedContentStoresNothingButReplies() {
        typeof(MapMetadataProperty).GetProperty(nameof(MapMetadataProperty.LimitContent))!.GetSetMethod(true)!.Invoke(session.Field!.Metadata.Property, [JobHandler.SkillTreeLimitContent]);

        Send(9, writer => {
            writer.WriteInt(1);
            writer.WriteInt(10200001);
            writer.WriteShort(1);
            writer.WriteBool(true);
        });

        Assert.That(Ops().Count(op => op == SendOp.Job), Is.EqualTo(1));
        Assert.That(session.Config.Skill.SkillBook.SkillTabs.SelectMany(tab => tab.Skills), Is.Empty);
    }

    [Test]
    public void AutoDistribute_RepliesWithTheStoredTree() {
        Send(11, writer => writer.WriteInt(0));

        Assert.That(Ops().Count(op => op == SendOp.Job), Is.EqualTo(1));
    }

    [Test]
    public void Reset_EveryRankReplies() {
        Send(10, writer => writer.WriteInt(0));

        Assert.That(Ops().Count(op => op == SendOp.Job), Is.EqualTo(1));
    }

    [Test]
    public void Reset_OneRankNeedsTheFeature() {
        test.SetFeaturesOff(JobHandler.RankResetFeature);

        Send(10, writer => writer.WriteInt(1));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Reset_UnknownRankIsIgnored() {
        Send(10, writer => writer.WriteInt(9));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Advance_WithoutTheNpcOrItsScriptDoesNothing() {
        Send(2, writer => writer.WriteInt(12345));

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Player.Value.Character.Job, Is.EqualTo(Job.Newbie));
    }
}
