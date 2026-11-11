using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// RequestCube 68 shows a blueprint in the model home (UIBlueprintMarketDialog::Preview,
// uiblueprintmarketdialog.cpp:556). It is a preview: it never builds in a real home or in the other planners,
// only on a model-home plot, and only a blueprint the player can see, one on the market or one in the bag.
public static class BlueprintPreviewRule {
    public static bool PlotAllows(PlotMode mode) {
        return mode == PlotMode.ModelHome;
    }

    // A model home that still holds cubes (an earlier preview) is cleared first; the build follows on the client's
    // second request, which ResponseCube 0x45 asks for.
    public static bool ClearsFirst(int cubeCount) {
        return cubeCount > 0;
    }

    public static bool BlueprintAllows(bool listedOnMarket, bool inOwnBag) {
        return listedOnMarket || inOwnBag;
    }
}
