using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// The login scene's profile picture (Ugc 11) writes only the picture: the character's mesos and event merets are
// kept (the save of the whole character stored them as 0, since the game character has no currency to convert).
public class ProfilePictureTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;
    private UgcHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
        typeof(LoginSession).GetProperty(nameof(LoginSession.CharacterId))!.SetValue(session, test.CharacterId);
        handler = new UgcHandler { GameStorage = test.Storage };
        using var context = new Ms2Context(test.Rows.Options);
        Maple2.Database.Model.Character row = context.Character.Single(character => character.Id == test.CharacterId);
        row.Currency = new Maple2.Database.Model.CharacterCurrency { Meso = 777, EventMeret = 55, ValorToken = 3 };
        row.Profile = new Maple2.Database.Model.Profile { Motto = "motto", Picture = "old", Title = 9 };
        context.SaveChanges();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void SetPicture(string picture) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(11);
            writer.WriteUnicodeString(picture);
        }));
    }

    private Maple2.Database.Model.Character Stored() {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.AsNoTracking().Single(character => character.Id == test.CharacterId);
    }

    [Test]
    public void Picture_KeepsCurrency() {
        SetPicture("/profile/new.png");

        Maple2.Database.Model.Character stored = Stored();
        Assert.Multiple(() => {
            Assert.That(stored.Profile.Picture, Is.EqualTo("/profile/new.png"));
            Assert.That((stored.Currency.Meso, stored.Currency.EventMeret, stored.Currency.ValorToken), Is.EqualTo((777L, 55L, 3L)));
            Assert.That((stored.Profile.Motto, stored.Profile.Title), Is.EqualTo(("motto", 9)));
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Ugc }));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A whole-character save writes no currency either.
    [Test]
    public void SaveCharacter_KeepsCurrency() {
        using (GameStorage.Request db = test.Storage.Context()) {
            Character character = db.GetCharacter(test.CharacterId, test.AccountId)!;
            character.Motto = "changed";
            Assert.That(db.SaveCharacter(character), Is.True);
        }

        Maple2.Database.Model.Character stored = Stored();
        Assert.Multiple(() => {
            Assert.That(stored.Profile.Motto, Is.EqualTo("changed"));
            Assert.That((stored.Currency.Meso, stored.Currency.EventMeret), Is.EqualTo((777L, 55L)));
        });
    }

    // Another account's character is not changed.
    [Test]
    public void OtherAccountsCharacter_IsNotChanged() {
        long other = test.AddCharacter("Other", test.AccountId + 1);
        typeof(LoginSession).GetProperty(nameof(LoginSession.CharacterId))!.SetValue(session, other);

        SetPicture("/profile/new.png");

        using var context = new Ms2Context(test.Rows.Options);
        Assert.That(context.Character.AsNoTracking().Single(character => character.Id == other).Profile.Picture, Is.Not.EqualTo("/profile/new.png"));
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
