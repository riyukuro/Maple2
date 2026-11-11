namespace Maple2.Model.Validators;

public static class PetRules {
    // RequestPet 4 (rename): the client's name box sends a name that is not empty and is shorter than 32 characters.
    public const int MaxNameLength = 31;

    public static bool IsName(string name) {
        return name.Length > 0 && name.Length <= MaxNameLength;
    }

    // The client also runs its pet name filter; the server runs its ban-word list.
    public static bool IsAllowedName(string name, Func<string, bool> isBanned) {
        return IsName(name) && !string.IsNullOrWhiteSpace(name) && !isBanned(name);
    }

    // A name the client's box can send but the server's ban-word list refuses. The client's own filter
    // passed it, so the player has seen no error yet and the server has to show one.
    public static bool IsBannedName(string name, Func<string, bool> isBanned) {
        return IsName(name) && !string.IsNullOrWhiteSpace(name) && isBanned(name);
    }

    // The client asks for no merets while the pet has a rename left, and for the pet rename price after that.
    public static int RenameCost(short renameRemaining, int price) {
        return renameRemaining > 0 ? 0 : price;
    }
}
