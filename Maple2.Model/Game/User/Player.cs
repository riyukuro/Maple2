using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;

namespace Maple2.Model.Game;

public class Player {
    public readonly Account Account;
    public readonly Character Character;
    public readonly int ObjectId;

    public required Home Home { get; set; }
    public required Currency Currency { get; init; }
    public required Unlock Unlock { get; init; }

    public Player(Account account, Character character, int objectId) {
        Account = account;
        Character = character;
        ObjectId = objectId;
    }
}

public class Unlock {
    public DateTime LastModified { get; set; }

    public IDictionary<InventoryType, short> Expand { get; init; } = new Dictionary<InventoryType, short>();
    public short HairSlotExpand;

    public readonly ISet<int> Maps = new SortedSet<int>();
    public readonly ISet<int> Taxis = new SortedSet<int>();
    public readonly ISet<int> Titles = new SortedSet<int>();
    // Titles is changed by the owner's session and read by field threads that build another
    // player's view of the owner (the name-tag symbol flag). Those two go through here.
    public bool AddTitle(int titleId) {
        lock (Titles) {
            return Titles.Add(titleId);
        }
    }
    public bool HasTitle(int titleId) {
        lock (Titles) {
            return Titles.Contains(titleId);
        }
    }
    public int[] TitlesCopy() {
        lock (Titles) {
            return Titles.ToArray();
        }
    }
    public readonly IList<int> Emotes = new List<int>();
    // Learn time and favourite mark of the emotes above, by emote id. An emote without a row has neither.
    public readonly IDictionary<int, EmoteState> EmoteStates = new SortedDictionary<int, EmoteState>();

    public Emote GetEmote(int emoteId) {
        return Emote.Of(emoteId, EmoteStates.TryGetValue(emoteId, out EmoteState? state) ? state : null);
    }

    // Adds an emote with its learn time; false when it is already held.
    public bool LearnEmote(int emoteId, long nowSeconds) {
        if (Emotes.Contains(emoteId)) {
            return false;
        }
        Emotes.Add(emoteId);
        EmoteStates[emoteId] = new EmoteState { LearnTime = nowSeconds };
        return true;
    }

    // Emote mode 4: the favourite mark of an emote. Only a held emote takes one.
    public bool SetEmoteFavorite(int emoteId, bool favorite) {
        if (!Emotes.Contains(emoteId)) {
            return false;
        }
        if (EmoteStates.TryGetValue(emoteId, out EmoteState? state)) {
            state.Favorite = favorite;
        } else if (favorite) {
            EmoteStates[emoteId] = new EmoteState { Favorite = true };
        }
        return true;
    }
    public readonly IDictionary<int, long> StickerSets = new Dictionary<int, long>();
    public readonly IDictionary<int, bool> MasteryRewardsClaimed = new Dictionary<int, bool>();
    public readonly IDictionary<int, DungeonRankReward> DungeonRankRewards = new Dictionary<int, DungeonRankReward>();
    public readonly IDictionary<int, short> Pets = new SortedDictionary<int, short>();
    public readonly IDictionary<int, FishEntry> FishAlbum = new Dictionary<int, FishEntry>();

    // Used for trophies
    public readonly ISet<int> InteractedObjects = new SortedSet<int>();
    public readonly IDictionary<int, byte> CollectedItems = new Dictionary<int, byte>();

    // The alliance fame points (reputation), by alliance: the client's GetFamePoints, sent by UserEnv 10 and 11.
    public readonly IDictionary<AllianceType, int> FamePoints = new SortedDictionary<AllianceType, int>();

    // The fame log (the fame board's history), log id to the time it was added in epoch seconds: UserEnv 12 and 13.
    public readonly IDictionary<int, long> FameLogs = new SortedDictionary<int, long>();
}

public class Currency {
    public long Meret;
    public long GameMeret;
    public long Meso;
    public long EventMeret;
    public long ValorToken;
    public long Treva;
    public long Rue;
    public long HaviFruit;
    public long ReverseCoin;
    public long MentorToken;
    public long MenteeToken;
    public long StarPoint;
    public long MesoToken;
};
