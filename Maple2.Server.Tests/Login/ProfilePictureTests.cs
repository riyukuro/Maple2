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
// The packet carries only the web server's path, which names the character (any of the account's characters in the
// list, not only one made in this session); a path the web server did not answer is not stored.
public class ProfilePictureTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;
    private UgcHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
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

    private static string PathOf(long characterId) => $"data/profiles/avatar/{characterId}/new-01.png";

    private string Picture(long characterId) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.AsNoTracking().Single(character => character.Id == characterId).Profile.Picture;
    }

    [Test]
    public void Picture_KeepsCurrency() {
        SetPicture(PathOf(test.CharacterId));

        Maple2.Database.Model.Character stored = Stored();
        Assert.Multiple(() => {
            Assert.That(stored.Profile.Picture, Is.EqualTo(PathOf(test.CharacterId)));
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

        SetPicture(PathOf(other));

        Assert.That(Picture(other), Is.Not.EqualTo(PathOf(other)));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The character the path names gets the picture, not the one made last in this session.
    [Test]
    public void ThePathsCharacter_GetsThePicture() {
        long created = test.AddCharacter("Created");
        typeof(LoginSession).GetProperty(nameof(LoginSession.CharacterId))!.SetValue(session, created);

        SetPicture(PathOf(test.CharacterId));

        Assert.Multiple(() => {
            Assert.That(Picture(test.CharacterId), Is.EqualTo(PathOf(test.CharacterId)));
            Assert.That(Picture(created), Is.Not.EqualTo(PathOf(test.CharacterId)));
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Ugc }));
        });
    }

    // Text the web server never answers is not made the picture (every player that sees the character is shown it).
    [TestCase("/profile/new.png")]
    [TestCase("http://elsewhere/x.png")]
    [TestCase("data/profiles/avatar/{0}/../x.png")]
    [TestCase("data/profiles/avatar/{0}/new.jpg")]
    [TestCase("data/profiles/avatar/0{0}/new.png")]
    [TestCase("data/profiles/avatar/+{0}/new.png")]
    public void APathTheWebServerDidNotAnswer_IsNotStored(string format) {
        SetPicture(string.Format(format, test.CharacterId));

        Assert.That(Picture(test.CharacterId), Is.EqualTo("old"));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void APathLongerThanTheWebServersCap_IsNotStored() {
        SetPicture($"data/profiles/avatar/{test.CharacterId}/{new string('a', 200)}.png");

        Assert.That(Picture(test.CharacterId), Is.EqualTo("old"));
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
