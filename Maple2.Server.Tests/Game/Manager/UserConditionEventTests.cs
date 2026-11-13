using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Attachment = Maple2.Model.Metadata.UserConditionEventMetadata.Attachment;

namespace Maple2.Server.Tests.Game.Manager;

// The user condition event (UserCondition): the rows of the event's group (value1) in Table/userConditionEvent.xml
// are given once the character meets them: each row's mail with the attachments for every job and its own, and
// the last row's notice (UserConditionEvent 1); the login sends the preview row of the last row noticed.
public class UserConditionEventTests {
    private const int EventId = 50000;
    private const int Common = 59200078;
    private const int Knight = 59200080;
    private const int Berserker = 59200081;

    private static UserConditionEventMetadata Row(int id, int group, ConditionType type, long code, long target, int preview = 0,
            int context = 0, params Attachment[] attachments) =>
        new(id, group, type, code, target,
            preview > 0 ? new UserConditionEventMetadata.NotifyData(preview, "s_systemmail_notify_gift", "attachment", attachments) : null,
            context > 0 ? new UserConditionEventMetadata.MailData(context, 0, 0, attachments) : null);

    private static readonly UserConditionEventMetadata[] Rows = [
        Row(1, 1, ConditionType.exp_rate, 1, 1, preview: 6, context: 50000101, new Attachment(0, Common, 1, 1)),
        Row(2, 1, ConditionType.level, 0, 2, preview: 6),
        Row(7, 1, ConditionType.level, 0, 5, preview: 13, context: 50000102,
            new Attachment(0, Common, 1, 2), new Attachment(10, Knight, 1, 1), new Attachment(20, Berserker, 1, 1)),
        Row(77, 1, ConditionType.job, 10, 0, preview: 0, context: 50000100, new Attachment(10, Knight, 1, 3), new Attachment(20, Berserker, 1, 3)),
        // Another group's row is not given.
        Row(90, 3, ConditionType.level, 0, 1, preview: 91, context: 50000200, new Attachment(0, Common, 1, 9)),
    ];

    private static TestGameSession Test(int group = 1) {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Common), TestGameSession.Metadata(Knight), TestGameSession.Metadata(Berserker));
        test.SetTable(new UserConditionEventTable(Rows.ToDictionary(row => row.Id)));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long> {
            [1] = 100, [5] = 1000,
        }));
        test.AddEvent(EventId, GameEventType.UserCondition, new UserCondition(group, "https://example.com/"));
        return test;
    }

    private static (string Content, (int Id, int Amount)[] Items)[] Mails(TestGameSession test) {
        using var context = new Ms2Context(test.Options);
        long[] mailIds = context.Mail.Where(mail => mail.ReceiverId == test.CharacterId).OrderBy(mail => mail.Id).Select(mail => mail.Id).ToArray();
        using var db = test.Storage.Context();
        return mailIds.Select(id => db.GetMail(id, test.CharacterId)!)
            .Select(mail => (mail.Content, mail.Items.Select(item => (item.Id, item.Amount)).OrderBy(item => item.Id).ToArray()))
            .ToArray();
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static List<string> Notices(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.UserConditionEvent).Select(Convert.ToHexString).ToList();

    [Test]
    public void UserCondition_GivesEachRowOnce_WithTheJobsAttachments() {
        TestGameSession test = Test();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        Character character = session.Player.Value.Character;
        character.Level = 1;
        character.Exp = 0;
        character.Job = Job.Newbie;
        test.TakeSent();

        // At login: 0% of level 1 meets nothing.
        session.GameEvent.Update(1000);
        Assert.That(Mails(test), Is.Empty);
        Assert.That(Notices(test), Is.Empty);

        // 1% of level 1: row 1.
        character.Exp = 1;
        session.ConditionUpdate(ConditionType.exp);
        session.GameEvent.Update(2000);
        Assert.That(Mails(test), Is.EqualTo(new[] { ("50000101", new[] { (Common, 1) }) }));
        Assert.That(Notices(test), Is.EqualTo(new[] { Hex(UserConditionEventPacket.Notice(1)) }));

        // Level 5 as a knight: rows 2, 7 and 77 at once, with the knight's attachments only; one notice, the last
        // row with one (7).
        character.Level = 5;
        character.Exp = 0;
        character.Job = Job.Knight;
        session.ConditionUpdate(ConditionType.level, targetLong: 5);
        session.GameEvent.Update(3000);
        Assert.That(Mails(test).Skip(1), Is.EqualTo(new[] {
            ("50000102", new[] { (Common, 2), (Knight, 1) }),
            ("50000100", new[] { (Knight, 3) }),
        }));
        Assert.That(Notices(test), Is.EqualTo(new[] { Hex(UserConditionEventPacket.Notice(7)) }));

        // Nothing again.
        session.ConditionUpdate(ConditionType.level, targetLong: 5);
        session.GameEvent.Update(4000);
        Assert.That(Mails(test), Has.Length.EqualTo(3));
        Assert.That(Notices(test), Is.Empty);

        // Stored at once, for the character; the login sends row 7's preview.
        Dictionary<GameEventUserValueType, string> stored = test.StoredEventValues(EventId);
        Assert.That(stored.GetValueOrDefault(GameEventUserValueType.UserConditionDone), Is.EqualTo("1,2,7,77"));
        Assert.That(stored.GetValueOrDefault(GameEventUserValueType.UserConditionNotice), Is.EqualTo("7"));
        Assert.That(session.GameEvent.UserConditionPreview(), Is.EqualTo(13));
        Assert.That(test.Errors, Is.Empty);
    }

    // The live KR event runs group 2, which no row has: nothing is given and the login sends 0.
    [Test]
    public void UserCondition_GroupWithoutRows_GivesNothing() {
        TestGameSession test = Test(group: 2);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Player.Value.Character.Level = 50;

        session.GameEvent.Update(1000);

        Assert.That(Mails(test), Is.Empty);
        Assert.That(session.GameEvent.UserConditionPreview(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Notice_ModeOneAndTheRow() {
        Assert.That(Hex(UserConditionEventPacket.Notice(7)), Is.EqualTo("BD00" + "01" + "07000000"));
        Assert.That(Hex(UserConditionEventPacket.Load(13)), Is.EqualTo("BD00" + "00" + "0D000000"));
    }

    [Test]
    public void Rules_Conditions() {
        Assert.Multiple(() => {
            Assert.That(UserConditionRules.Met(Rows[1], 2, 0, 1), Is.True);
            Assert.That(UserConditionRules.Met(Rows[1], 1, 99, 1), Is.False);
            // exp_rate: the percent of that level, or any higher level.
            Assert.That(UserConditionRules.Met(Rows[0], 1, 0, 1), Is.False);
            Assert.That(UserConditionRules.Met(Rows[0], 1, 1, 1), Is.True);
            Assert.That(UserConditionRules.Met(Rows[0], 2, 0, 1), Is.True);
            // job: the job's code.
            Assert.That(UserConditionRules.Met(Rows[3], 10, 0, 10), Is.True);
            Assert.That(UserConditionRules.Met(Rows[3], 10, 0, 20), Is.False);
            Assert.That(UserConditionRules.ExpRate(999, 1000), Is.EqualTo(99));
            Assert.That(UserConditionRules.ExpRate(5, 0), Is.EqualTo(0));
            Assert.That(UserConditionRules.ForJob(Rows[2].Mail!.Attachments, 20).Select(attachment => attachment.ItemId),
                Is.EqualTo(new[] { Common, Berserker }));
            Assert.That(UserConditionRules.Ids("7,1,x,2"), Is.EquivalentTo(new[] { 1, 2, 7 }));
            Assert.That(UserConditionRules.Join([7, 1, 77]), Is.EqualTo("1,7,77"));
        });
    }

    // GameEventUserCondition's context: the event id, the group (value1) and the URL (value2).
    [Test]
    public void UserCondition_EventBytes() {
        var metadata = new GameEventMetadata(EventId, GameEventType.UserCondition, DateTime.Now, DateTime.Now.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, [], new UserCondition(2, "ab"), "", "", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        Assert.That(Convert.ToHexString(writer.ToArray()), Does.EndWith("50C30000" + "02000000" + "0200" + "61006200"));
    }

    [Test]
    public void Ingest_ReadsTheRows() {
        var document = new XmlDocument();
        document.LoadXml("<ms2>"
            + "<event id=\"7\" gameEventID=\"1\"><condition type=\"level\" code=\"\" target=\"5\"/>"
            + "<notify useNotify=\"1\" preview=\"13\" descKey=\"k\" descArg=\"attachment\"><attachment job=\"0\" itemID=\"59200079\" itemCount=\"1\"/></notify>"
            + "<mail useMail=\"1\" context=\"50000102\" meso=\"5\"><attachment job=\"10\" itemID=\"59200080\" itemRank=\"3\" itemCount=\"2\"/></mail></event>"
            + "<event id=\"8\" gameEventID=\"1\"><condition type=\"job\" code=\"10\" target=\"\"/><notify useNotify=\"0\" preview=\"1\"/>"
            + "<mail useMail=\"1\" context=\"50000100\"/></event>"
            + "<event id=\"9\" gameEventID=\"1\"><condition type=\"nothing_known\" target=\"1\"/></event>"
            + "<event id=\"0\" gameEventID=\"1\"><condition type=\"level\" target=\"1\"/></event>"
            + "</ms2>");

        UserConditionEventTable table = UserConditionEventTable(document);

        Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] { 7, 8 }));
        UserConditionEventMetadata row = table.Entries[7];
        Assert.Multiple(() => {
            Assert.That((row.Group, row.Type, row.Code, row.Target), Is.EqualTo((1, ConditionType.level, 0L, 5L)));
            Assert.That(row.Notify?.Preview, Is.EqualTo(13));
            Assert.That(row.Notify!.Attachments, Is.EqualTo(new[] { new Attachment(0, 59200079, 1, 1) }));
            Assert.That((row.Mail?.Context, row.Mail?.Meso), Is.EqualTo(((int?) 50000102, (long?) 5)));
            Assert.That(row.Mail!.Attachments, Is.EqualTo(new[] { new Attachment(10, 59200080, 3, 2) }));
            Assert.That(table.Entries[8].Notify, Is.Null);
            Assert.That(table.Entries[8].Code, Is.EqualTo(10));
        });
    }

    private static UserConditionEventTable UserConditionEventTable(XmlDocument document) => TableMapper.UserConditionEvents(document);
}
