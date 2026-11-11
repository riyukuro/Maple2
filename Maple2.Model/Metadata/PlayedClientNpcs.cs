using System.Xml;

namespace Maple2.Model.Metadata;

// The NPCs the played client knows, read from that client's own NPC data (npcdata/<nnn>.xml). The client keeps an
// <npc> only when its id is not 0 and one of its <environment> children passes the locale and feature test
// (NpcDataTableCli::Load, client/ms2/npcdatacli.cpp:311, with CNpcData::Load, commonfiles/npcdata.cpp:771, and
// FUN_14028f8d0, commonfiles/commonutil.cpp:1064). For any other id FindNpcData (npcdata.cpp:1047) finds nothing,
// and CSceneStateGame::OnFieldAddNpc (client/ms2/scenestategame.cpp:3533) then creates no NPC: the server's NPC
// would stand, fight and be counted where the player sees nothing.
public static class PlayedClientNpcs {
    public const string NpcFolder = "npcdata/";

    public static bool IsNpcFile(string name) => name.StartsWith(NpcFolder, StringComparison.OrdinalIgnoreCase);

    // The ids of the NPC data files. available is the environment filter (feature and locale).
    public static HashSet<int> Ids(IEnumerable<XmlDocument> npcFiles, Func<XmlElement, bool> available) =>
        PlayedClientItems.Ids(npcFiles, available, "npc");

    // Whether the server may spawn an NPC of this data: not when the played client lacks it
    // (NpcMetadataBasic.NotInPlayedClient). Without the mark (no played client given, or data ingested before the
    // mark) every NPC can be.
    public static bool CanSpawn(NpcMetadata metadata) => !metadata.Basic.NotInPlayedClient;
}
