namespace Maple2.File.Ingest.Helpers;

// The server data this fork is built and tested with (FORK.md, "Data and ingest").
public static class DataPack {
    public const string Url = "https://github.com/MS2Community/MapleStory2-XML/releases/tag/v1.3.2";

    public const string Hint = "Download Server.m2d, Server.m2h, Xml.m2d and Xml.m2h from " + Url +
                               " and put all four into MS2_DATA_FOLDER. Take that tag, not releases/latest: the latest release has no Server.m2d.";
}
