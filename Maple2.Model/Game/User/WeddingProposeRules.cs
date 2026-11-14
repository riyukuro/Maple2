using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// A proposal between two characters of the same gender is refused (0x15) where the WeddingBlockSex feature is on
// (IsGenderPairAllowed, weddingproxy.cpp:488; feature id 0x105, featuremanager.cpp:652), read from the ingested
// feature table (on in KR, off in NA).
public static class WeddingProposeRules {
    public const string BlockSexFeature = "WeddingBlockSex";

    public static bool GenderPairAllowed(Gender a, Gender b, bool blockSex) => a != b || !blockSex;
}
