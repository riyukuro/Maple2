using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Ugc 12 (CUgcManager::SendUGCMode12, after the web server stored the wedding profile picture, upload kind 10): the
// path is stored as this partner's picture of a married couple, and the player gets the marriage (the picture paths
// are its two ASCII strings, WeddingProxy::Read) and Wedding 35 (UIWeddingDialog::SetOwnProfile).
public class WeddingProfileTests {
    private const string File = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private TestGameSession test = null!;
    private GameSession session = null!;
    private long partnerId;
    private long marriageId;

    [TearDown]
    public void TearDown() {
        session?.Dispose();
    }

    private void Start(bool married) {
        test = new TestGameSession();
        session = test.Start();
        using GameStorage.Request db = test.Storage.Context();
        Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
        var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
        partner.ReturnMaps.Push(2000062);
        partnerId = db.CreateCharacter(partner)!.Id;
        marriageId = db.CreateMarriage(test.CharacterId, partnerId)!.Id;
        if (married) {
            Assert.That(db.SetMarried(marriageId, DateTime.Now), Is.True);
        }
        session.Marriage.Marriage = db.GetMarriage(test.CharacterId)!;
        test.TakeSent();
    }

    private void Send(string path) {
        var handler = new UgcHandler { WebStorage = null!, ItemMetadata = null!, TableMetadata = null!, World = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteUnicodeString(path);
        }));
    }

    private string StoredFor(long viewer, bool own) {
        using GameStorage.Request db = test.Storage.Context();
        Marriage marriage = db.GetMarriage(viewer)!;
        return own ? marriage.Partner2.Profile : marriage.Partner1.Profile;
    }

    [Test]
    public void Married_StoresThePathAndRefreshesTheDialog() {
        Start(married: true);
        string path = $"data/profiles/wedding/{test.CharacterId}/{File}.png";

        Send(path);

        Assert.That(StoredFor(test.CharacterId, own: true), Is.EqualTo(path));
        Assert.That(StoredFor(partnerId, own: false), Is.EqualTo(path));
        Assert.That(StoredFor(partnerId, own: true), Is.Empty);
        Assert.That(session.Marriage.Marriage.Partner2.Profile, Is.EqualTo(path));
        byte[][] wedding = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding).ToArray();
        Assert.That(wedding, Has.Length.EqualTo(2));
        Assert.That(wedding[0][2], Is.EqualTo(0));
        Assert.That(wedding[1], Is.EqualTo(new byte[] { wedding[1][0], wedding[1][1], 35 }));
    }

    // The client uploads the picture only in state 2 (CUgcViewerWeddingProfile::vfunc_3).
    [Test]
    public void Engaged_StoresNothing() {
        Start(married: false);

        Send($"data/profiles/wedding/{test.CharacterId}/{File}.png");

        Assert.That(StoredFor(test.CharacterId, own: true), Is.Empty);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding), Is.Empty);
    }

    // Only a picture of the sender's own folder: not the partner's, not the profile picture's, not any other text.
    [TestCase("data/profiles/wedding/{partner}/" + File + ".png")]
    [TestCase("data/profiles/avatar/{self}/" + File + ".png")]
    [TestCase("http://example.com/x.png")]
    [TestCase("")]
    public void OtherPath_StoresNothing(string template) {
        Start(married: true);

        Send(template.Replace("{partner}", partnerId.ToString()).Replace("{self}", test.CharacterId.ToString()));

        Assert.That(StoredFor(test.CharacterId, own: true), Is.Empty);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding), Is.Empty);
    }
}
