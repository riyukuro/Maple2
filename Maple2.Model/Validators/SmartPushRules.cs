using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// SmartPush (c2s 0x0083). Every sender writes the same four fields (smartpush.cpp:326,
// stateinteractdata.cpp:733): i32 push id, i32 argument (the price package id of an effect push, the
// count of an auto interaction), i32 recipe id (0 on an effect push), bool "this extends a running effect".
public static class SmartPushRules {
    public const int MaxAutoCount = 1000;

    public static (int Id, int Arg, int RecipeId, bool Extend) Read(IByteReader packet) {
        int id = packet.ReadInt();
        int arg = packet.ReadInt();
        int recipeId = packet.ReadInt();
        bool extend = packet.ReadBool();
        return (id, arg, recipeId, extend);
    }

    // The client asks for min(owned items, gathers left); a count outside 1..MaxAutoCount is not one of its.
    public static bool AutoCountAllowed(int count) => count is > 0 and <= MaxAutoCount;

    // The price is tested before the required item is taken, so a push that cannot be paid costs nothing.
    public static bool Affords(long owned, long cost) => cost >= 0 && owned >= cost;
}
