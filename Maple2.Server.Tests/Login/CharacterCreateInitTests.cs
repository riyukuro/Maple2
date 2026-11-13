using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.Session;
using Maple2.Tools;
using static Maple2.Model.Error.CharacterCreateError;

namespace Maple2.Server.Tests.Login;

// A new character is its row, its unlocks, first skill tab and config, and its outfits, written together. When the
// set-up fails, the client gets the create error and no entry: the request's dispose rolls the character back.
public class CharacterCreateInitTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
        // The character list reads the account a create lists the new character under.
        session.ListCharacters();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private static Character NewCharacter() => new() {
        Name = "Fresh", Gender = Gender.Male, Job = Job.Newbie, Mastery = new Mastery(), MapId = 2000023,
        ReturnMaps = new LimitedStack<int>(3, 2000023),
    };

    private static string Hex(ByteWriter packet) => System.Convert.ToHexString(packet.ToArray());

    // The first skill tab's save fails once (the later saves of the request work): the client gets the create error,
    // not an entry (the character used to be listed and committed without its skill tab and config).
    [Test]
    public void CreateCharacter_SetUpNotSaved_SendsTheCreateError() {
        bool failed = false;
        test.Rows.FailSave = context => {
            if (failed || !context.ChangeTracker.Entries<Maple2.Database.Model.SkillTab>().Any()) {
                return false;
            }
            failed = true;
            return true;
        };

        session.CreateCharacter(NewCharacter(), new List<Item>());

        Assert.That(failed, Is.True);
        Assert.That(test.TakeSent().Select(packet => System.Convert.ToHexString(packet)),
            Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(s_char_err_system)) }));
    }

    [Test]
    public void CreateCharacter_Saved_SendsTheEntry() {
        session.CreateCharacter(NewCharacter(), new List<Item>());

        Assert.That(test.TakeSentOps(), Has.Member(SendOp.CharacterList));
        Assert.That(test.Errors, Is.Empty);
    }
}
