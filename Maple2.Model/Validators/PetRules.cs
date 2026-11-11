namespace Maple2.Model.Validators;

public static class PetRules {
    // RequestPet 4 (rename): the client's name box sends a name that is not empty and is shorter than 32 characters.
    public const int MaxNameLength = 31;

    public static bool IsName(string name) {
        return name.Length > 0 && name.Length <= MaxNameLength;
    }

    // The client asks for no merets while the pet has a rename left, and for the pet rename price after that.
    public static int RenameCost(short renameRemaining, int price) {
        return renameRemaining > 0 ? 0 : price;
    }
}
