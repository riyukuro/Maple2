using System.Collections.Concurrent;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Game.Model.Widget;

public class SceneMovieWidget : Widget {
    // play_scene_movie skip_type="needAll": IsStop holds only once every player in the field has stopped the movie.
    private volatile bool needAll;
    private readonly ConcurrentDictionary<long, int> stopped = new();

    public SceneMovieWidget(FieldManager field) : base(field) {
        Conditions = new ConcurrentDictionary<string, int>();
    }

    public void Play(bool needAll) {
        this.needAll = needAll;
        stopped.Clear();
    }

    // The client's UICutSceneMovie::Exit sends the stop (skip confirmed or movie stopped) for its own player.
    public void Stop(long characterId, int value) {
        stopped[characterId] = value;
        Conditions["IsStop"] = value;
    }

    public override bool Check(string name, string arg) {
        if (!int.TryParse(arg, out int value)) {
            return false;
        }
        if (needAll && name == "IsStop") {
            ICollection<FieldPlayer> players = Field.Players.Values;
            return players.Count > 0 && players.All(player => stopped.TryGetValue(player.Value.Character.Id, out int stop) && stop == value);
        }
        return Conditions.GetValueOrDefault(name) == value;
    }

    public override void Action(string function, int numericArg, string stringArg) {
        switch (function) {
            case "Clear":
                Clear();
                break;
        }
    }

    private void Clear() {
        Conditions.Clear();
        stopped.Clear();
    }
}
