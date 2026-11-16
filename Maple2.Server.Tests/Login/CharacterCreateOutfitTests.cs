using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using static Maple2.Model.Error.CharacterCreateError;

namespace Maple2.Server.Tests.Login;

// The outfit of a create (CharacterManagement 1): the client writes each item the player kept on the create screen as
// its id, its slot and its appearance (the colour, and the hair or cap positions). Each must be a default item of its
// slot for every job or for the picked one, an item the server knows, of the picked gender, one per slot.
public class CharacterCreateOutfitTests {
    private const int Top = 11400001;      // CL, a default of every job
    private const int Hair = 10200001;     // HR, a Knight default
    private const int Dress = 11400002;    // CL, a default, female only
    private const int Missing = 11400003;  // CL, a default the item data does not hold
    private const int Shield = 14000001;   // OH

    private TestLoginSession test = null!;
    private CharacterManagementHandler handler = null!;
    private LoginSession session = null!;
    private BanWordStorage banWords = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tutorial = new JobTable.Tutorial(2000023, 0, 0, [], [], [], []);
        var skills = new Dictionary<SkillRank, JobTable.Skill[]> { [SkillRank.Basic] = [], [SkillRank.Awakening] = [] };
        test.Rows.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Knight] = new(tutorial, skills, []),
            [JobCode.Berserker] = new(tutorial, skills, []),
        }));
        test.Rows.SetTable(new DefaultItemsTable(
            new Dictionary<EquipSlot, int[]> { [EquipSlot.CL] = [Top, Dress, Missing], [EquipSlot.OH] = [Shield] },
            new Dictionary<JobCode, IReadOnlyDictionary<EquipSlot, int[]>> {
                [JobCode.Knight] = new Dictionary<EquipSlot, int[]> { [EquipSlot.HR] = [Hair] },
            }));
        test.Rows.Register(TestGameSession.Metadata(Top, EquipSlot.CL), TestGameSession.Metadata(Hair, EquipSlot.HR),
            TestGameSession.Metadata(Dress, EquipSlot.CL, Gender.Female), TestGameSession.Metadata(Shield, EquipSlot.OH));
        handler = new CharacterManagementHandler {
            World = test.World, GameStorage = test.Storage, BanWordStorage = banWords = new BanWordStorage(words),
            ItemMetadata = test.Rows.ItemMetadata, TableMetadata = test.Rows.TableMetadata, ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        session = test.Start();
        using (var context = new Ms2Context(test.Rows.Options)) {
            context.Account.Find(test.AccountId)!.MaxCharacters = 4;
            context.SaveChanges();
        }
        session.ListCharacters();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private static void Color(Maple2.PacketLib.Tools.ByteWriter writer, int index) {
        writer.WriteInt(unchecked((int) 0xFF102030));
        writer.WriteInt(unchecked((int) 0xFF405060));
        writer.WriteInt(unchecked((int) 0xFF708090));
        writer.WriteInt(index);
        writer.WriteInt(0);
    }

    // Each item: its id, its slot byte and its appearance; a hair adds its two lengths and four positions.
    private void Create(JobCode job, string name, Gender gender, params (int id, EquipSlot slot)[] items) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte((byte) gender);
            writer.WriteShort((short) job);
            writer.WriteUnicodeString(name);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteShort(1);
            writer.WriteByte((byte) items.Length);
            foreach ((int id, EquipSlot slot) in items) {
                writer.WriteInt(id);
                writer.WriteByte((byte) slot);
                Color(writer, 3);
                if (slot == EquipSlot.HR) {
                    writer.WriteFloat(1.5f);
                    for (int i = 0; i < 6; i++) {
                        writer.WriteFloat(i);
                    }
                    writer.WriteFloat(2.5f);
                    for (int i = 0; i < 6; i++) {
                        writer.WriteFloat(-i);
                    }
                }
            }
            writer.WriteInt(0);
        }));
    }

    private int Count(string name) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.Count(character => character.Name == name);
    }

    private List<(int, short, ItemGroup)> Outfit(string name) {
        using var context = new Ms2Context(test.Rows.Options);
        long id = context.Character.Single(character => character.Name == name).Id;
        return context.Item.Where(item => item.OwnerId == id).AsEnumerable()
            .Select(item => (item.ItemId, item.Slot, item.Group)).OrderBy(item => item.ItemId).ToList();
    }

    private static string Error(Maple2.Model.Error.CharacterCreateError error) =>
        Convert.ToHexString(CharacterListPacket.CreateError(error).ToArray());

    [Test]
    public void KeptOutfit_IsWornByTheNewCharacter() {
        Create(JobCode.Knight, "Fresh", Gender.Male, (Top, EquipSlot.CL), (Hair, EquipSlot.HR));

        Assert.Multiple(() => {
            Assert.That(Count("Fresh"), Is.EqualTo(1));
            Assert.That(Outfit("Fresh"), Is.EqualTo(new[] {
                (Hair, (short) EquipSlot.HR, ItemGroup.Outfit), (Top, (short) EquipSlot.CL, ItemGroup.Outfit),
            }));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    [Test]
    public void FemaleOnlyDefault_IsKeptByAFemaleCharacter() {
        Create(JobCode.Knight, "Fresh", Gender.Female, (Dress, EquipSlot.CL));

        Assert.That(Outfit("Fresh"), Is.EqualTo(new[] { (Dress, (short) EquipSlot.CL, ItemGroup.Outfit) }));
    }

    // A running CharacterFirstConnect row mails the new character its system mail.
    [Test]
    public void FirstConnectEvent_MailsTheNewCharacter() {
        test.AddEvent(9001, GameEventType.CharacterFirstConnect, new Maple2.Model.Metadata.CharacterFirstConnectMail(110000));

        Create(JobCode.Knight, "Fresh", Gender.Male, (Top, EquipSlot.CL));

        using var context = new Ms2Context(test.Rows.Options);
        long id = context.Character.Single(character => character.Name == "Fresh").Id;
        Assert.That(context.Mail.Where(mail => mail.ReceiverId == id).AsEnumerable().Select(mail => mail.Content), Is.EqualTo(new[] { "110000" }));
    }

    // A weapon slot (and the off hand of a default table) is never an outfit item of the create screen; another job's
    // default, an item the data lacks, an item of the other gender and two items in one slot are not either.
    private static IEnumerable<TestCaseData> Refused() {
        yield return new TestCaseData(JobCode.Knight, Gender.Male, new[] { (Shield, EquipSlot.OH) }).SetName("OffHand");
        yield return new TestCaseData(JobCode.Knight, Gender.Male, new[] { (Top, EquipSlot.Unknown) }).SetName("UnknownSlot");
        yield return new TestCaseData(JobCode.Knight, Gender.Male, new[] { (Top, EquipSlot.PA) }).SetName("DefaultInAnotherSlot");
        yield return new TestCaseData(JobCode.Berserker, Gender.Male, new[] { (Hair, EquipSlot.HR) }).SetName("AnotherJobsDefault");
        yield return new TestCaseData(JobCode.Knight, Gender.Male, new[] { (Missing, EquipSlot.CL) }).SetName("NotInTheItemData");
        yield return new TestCaseData(JobCode.Knight, Gender.Male, new[] { (Dress, EquipSlot.CL) }).SetName("FemaleOnlyOnAMale");
        yield return new TestCaseData(JobCode.Knight, Gender.Male, new[] { (Top, EquipSlot.CL), (Top, EquipSlot.CL) }).SetName("SlotTwice");
    }

    [TestCaseSource(nameof(Refused))]
    public void InvalidOutfit_IsRefusedWithTheDefaultItemText(JobCode job, Gender gender, (int, EquipSlot)[] items) {
        Create(job, "Fresh", gender, items);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(s_char_err_invalid_def_item) }));
            Assert.That(Count("Fresh"), Is.Zero);
        });
    }

    // A create before the character list was sent (no account read yet) has no slot count: the system text.
    [Test]
    public void BeforeTheAccountIsKnown_SystemText() {
        session.Dispose();
        test = new TestLoginSession();
        handler = new CharacterManagementHandler {
            World = test.World, GameStorage = test.Storage, BanWordStorage = banWords,
            ItemMetadata = test.Rows.ItemMetadata, TableMetadata = test.Rows.TableMetadata, ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        session = test.Start(init: false);

        Create(JobCode.Knight, "Fresh", Gender.Male);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(s_char_err_system) }));
    }
}
