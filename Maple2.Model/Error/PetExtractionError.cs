// ReSharper disable InconsistentNaming, IdentifierTypo

namespace Maple2.Model.Error;

// The result byte of the pet extraction answer (PetSkinBadge), as UIPetExtractionScrollDialog reads it
// (uipetextractionscrolldialog.cpp:229): 0 the badge was made, 2 the inventory is full, any other code invalid.
public enum PetExtractionError : byte {
    ok = 0,
    s_petextraction_inventoryfull = 2,
    s_petextraction_invalid = 3,
}
