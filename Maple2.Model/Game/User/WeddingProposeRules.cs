using System;
using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// A proposal between two characters of the same gender is refused (0x15) where the WeddingBlockSex feature is on
// (IsGenderPairAllowed, weddingproxy.cpp:488; feature id 0x105, featuremanager.cpp:652). table/feature.xml opens it
// at KR 54, CN 58 and TW 99, and table/feature_setting.xml's Live levels are KR 56, CN 59 and TW 99; JP, NA and TH
// (feature 99, Live 39, 47 and 1) leave it off.
public static class WeddingProposeRules {
    private static readonly string[] BlockSexLocales = ["KR", "CN", "TW"];

    public static bool BlocksSameGender(string locale) => Array.IndexOf(BlockSexLocales, locale) >= 0;

    public static bool GenderPairAllowed(Gender a, Gender b, string locale) => a != b || !BlocksSameGender(locale);
}
