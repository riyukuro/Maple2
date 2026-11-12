using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

// What is kept for an emote besides its id: when it was learned (epoch seconds) and its favourite mark.
public sealed class EmoteState {
    public long LearnTime { get; set; }
    public bool Favorite { get; set; }
}

// One entry of the client's emote list (OnEmote mode 0: i32 id, i32 level, bool permanent, i64 expiry,
// i64 learn time, bool favourite; 26 bytes). The client lists permanent emotes first by id and the others by
// learn time, then id. KMS2 sends a character's starting emotes as permanent with both times 0, and a learned
// one as not permanent with expiry 0 and its learn time.
public readonly struct Emote {
    public readonly int Id;
    public readonly int Level = 1;
    public readonly bool Permanent;
    public readonly long ExpiryTime;
    public readonly long LearnTime;
    public readonly bool Favorite;

    public Emote(int id, bool permanent = false, long learnTime = 0, bool favorite = false, long expiryTime = 0) {
        Id = id;
        Permanent = permanent;
        LearnTime = learnTime;
        Favorite = favorite;
        ExpiryTime = expiryTime;
    }

    // The entry for an emote the character holds. An emote learned before the learn time was kept has no state
    // and goes out with learn time 0.
    public static Emote Of(int id, EmoteState? state) {
        bool permanent = Array.IndexOf(Constant.DefaultEmotes, id) >= 0;
        return new Emote(id, permanent, permanent ? 0 : state?.LearnTime ?? 0, state?.Favorite ?? false);
    }
}
