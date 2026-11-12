using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;

namespace Maple2.Server.Tests.Core;

// The world queues a game server's player updates and applies them every two seconds (PlayerInfoLookup.Sync).
// Each is compared with the record when it is applied, so updates queued in one window see each other.
public class PlayerInfoQueuedUpdateTests {
    private static PlayerInfo Record() {
        return new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) {
            Channel = 1,
            MapId = 2000023,
            CurrentHp = 100,
            TotalHp = 100,
        };
    }

    private static PlayerUpdateRequest Death(DeathState state) {
        return new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, DeathState = (int) state, Async = true };
    }

    private static PlayerUpdateRequest Health(long current) {
        return new PlayerUpdateRequest {
            AccountId = 1,
            CharacterId = 2,
            Health = new HealthUpdate { CurrentHp = current, TotalHp = 100 },
            Async = true,
        };
    }

    // Dead and alive again inside one window: the record ends alive, and the next death is a change again.
    [Test]
    public void DeadThenAliveInOneWindow_EndsAlive() {
        PlayerInfo info = Record();

        PlayerInfoUpdateEvent? dead = info.ApplyChanged(Death(DeathState.FirstDeath));
        PlayerInfoUpdateEvent? alive = info.ApplyChanged(Death(DeathState.Alive));

        Assert.That(dead?.Type, Is.EqualTo(UpdateField.Death));
        Assert.That(alive?.Type, Is.EqualTo(UpdateField.Death));
        Assert.That(info.DeathState, Is.EqualTo(DeathState.Alive));

        PlayerInfoUpdateEvent? again = info.ApplyChanged(Death(DeathState.Metal));
        Assert.That(again?.Type, Is.EqualTo(UpdateField.Death));
        Assert.That(info.DeathState, Is.EqualTo(DeathState.Metal));
    }

    // The same two requests compared when they are queued: the second finds the record still alive and is dropped,
    // so the record stays dead and the next death is no change.
    [Test]
    public void ComparedWhenQueued_SecondIsLost() {
        PlayerInfo info = Record();

        var dead = new PlayerInfoUpdateEvent(info, Death(DeathState.FirstDeath));
        var alive = new PlayerInfoUpdateEvent(info, Death(DeathState.Alive));

        Assert.That(dead.Type, Is.EqualTo(UpdateField.Death));
        Assert.That(alive.Type, Is.EqualTo(UpdateField.None));
    }

    [Test]
    public void HealthDownAndUpInOneWindow_EndsFull() {
        PlayerInfo info = Record();

        Assert.That(info.ApplyChanged(Health(40))?.Type, Is.EqualTo(UpdateField.Health));
        Assert.That(info.ApplyChanged(Health(100))?.Type, Is.EqualTo(UpdateField.Health));
        Assert.That(info.CurrentHp, Is.EqualTo(100));
    }

    [Test]
    public void MapAwayAndBackInOneWindow_EndsBack() {
        PlayerInfo info = Record();

        Assert.That(info.ApplyChanged(new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, MapId = 2000062, Async = true })?.Type, Is.EqualTo(UpdateField.Map));
        Assert.That(info.ApplyChanged(new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, MapId = 2000023, Async = true })?.Type, Is.EqualTo(UpdateField.Map));
        Assert.That(info.MapId, Is.EqualTo(2000023));
    }

    [Test]
    public void SameValues_ChangeNothing() {
        PlayerInfo info = Record();

        Assert.That(info.ApplyChanged(Health(100)), Is.Null);
        Assert.That(info.ApplyChanged(Death(DeathState.Alive)), Is.Null);
    }
}
