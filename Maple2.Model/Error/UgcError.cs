// ReSharper disable InconsistentNaming, IdentifierTypo

namespace Maple2.Model.Error;

// The error byte of Ugc 0, as CUgcManager::FUN_14188f450 (ugcmanager_4.cpp:3805) reads it. Every value unlocks
// the client's upload; the named ones show the client's own text (the client string table id in brackets).
public enum UgcError : byte {
    none = 0,
    s_err_lack_meso = 0x13, // [52]
    s_err_inventory = 0x81, // [67]
    // Chosen here: 0x8a is a case of the client's switch that shows nothing and only unlocks the upload, used
    // for a refusal that has no text of its own in the switch (or whose text the server sends itself).
    Refused = 0x8a,
}
