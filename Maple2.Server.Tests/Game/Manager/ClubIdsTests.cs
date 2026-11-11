using Maple2.Model.Game;

namespace Maple2.Server.Tests.Game.Manager;

public class ClubIdsTests {
    // The login load lists a character's clubs and each club manager then reports its own id: the list
    // keeps one entry per club (client/ms2 clubcommon.cpp:208 stops on more entries than the club limit).
    [Test]
    public void ClubIsListedOnce() {
        var character = new Character { Name = "club", Mastery = new Mastery() };
        character.ClubIds = [101, 102];

        Assert.That(character.AddClubId(101), Is.False);
        Assert.That(character.AddClubId(102), Is.False);
        Assert.That(character.AddClubId(103), Is.True);
        Assert.That(character.ClubIds, Is.EqualTo(new long[] { 101, 102, 103 }));
    }
}
