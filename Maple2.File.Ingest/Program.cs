using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using M2dXmlGenerator;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Model.Metadata;
using Maple2.File.Ingest;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Ingest.Utils;
using Maple2.File.IO;
using Maple2.File.IO.Nif;
using Maple2.File.Parser.Flat;
using Maple2.File.Parser.MapXBlock;
using Maple2.File.Parser.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

string language = "en";

Console.OutputEncoding = System.Text.Encoding.UTF8;

bool runNavmesh = false;
bool dropData = false;

foreach (string arg in args) {
    switch (arg) {
        case "--run-navmesh":
            runNavmesh = true;
            break;
        case "--drop-data":
            dropData = true;
            break;
    }
}

// Force Globalization to en-US because we use periods instead of commas for decimals
CultureInfo.CurrentCulture = new CultureInfo("en-US");

DotEnv.Load();

string? languageEnv = Environment.GetEnvironmentVariable("LANGUAGE");
if (languageEnv == null) {
    throw new ArgumentException("LANGUAGE environment variable was not set");
}
language = languageEnv.ToLower();

// The feature.xml / feature_setting.xml column the data is filtered by (Maple2.Tools.Locale, shared with the server).
if (!Locale.TryRead(out string locale, out string localeError)) {
    Console.Error.WriteLine(localeError);
    Environment.Exit(1);
}

// The feature_setting.xml row (server type) the features are read at: MS2_FEATURE_ENV, unset = Live.
if (!FeatureLevels.TryReadEnv(Environment.GetEnvironmentVariable(FeatureLevels.EnvOption), out string env)) {
    Console.Error.WriteLine($"{FeatureLevels.EnvOption} is not one of {string.Join(", ", FeatureLevels.Envs)} (unset or empty = {FeatureLevels.DefaultEnv})");
    Environment.Exit(1);
}

string? ms2Root = Environment.GetEnvironmentVariable("MS2_DATA_FOLDER");
if (ms2Root == null) {
    throw new ArgumentException("MS2_DATA_FOLDER environment variable was not set");
}

string xmlPath = Path.Combine(ms2Root, "Xml.m2d");
string exportedPath = Path.Combine(ms2Root, "Resource/Exported.m2d");
string serverPath = Path.Combine(ms2Root, "Server.m2d");

if (!File.Exists(xmlPath)) {
    throw new FileNotFoundException($"Could not find Xml.m2d file at path: {xmlPath}\n" + DataPack.Hint);
}

// Optional: the Xml.m2d of the client that is played, when it is another version than the data folder's.
// Read from it: the client rows of the constants table (prices, the client's cooltimes and caps), and the
// field table, whose empty rows mark the maps that client cannot load.
string? playedXmlPath = Maple2.Model.Metadata.ClientPriceConstants.PackOption(Environment.GetEnvironmentVariable("MS2_PLAYED_CLIENT_XML"));
if (playedXmlPath != null && !File.Exists(playedXmlPath)) {
    throw new FileNotFoundException($"MS2_PLAYED_CLIENT_XML is set, but there is no file at path: {playedXmlPath}");
}

// Optional: read the server pack's original (KMS) drop table instead of today's (ServerDropTable).
bool originalDropTable = Maple2.File.Ingest.Helpers.ServerDropTable.UseOriginal(Environment.GetEnvironmentVariable(Maple2.File.Ingest.Helpers.ServerDropTable.Option));
// Optional: keep one ended attendance gift event, moved to start on the ingest day (AttendGiftReplay).
bool replayAttendGift = Maple2.File.Ingest.Helpers.AttendGiftReplay.Enabled(Environment.GetEnvironmentVariable(Maple2.File.Ingest.Helpers.AttendGiftReplay.Option));
// Optional: a directory of server pack files read instead of the pack's (ServerOverlay).
var serverOverlay = Maple2.File.Ingest.Helpers.ServerOverlay.FromEnvironment();
if (serverOverlay.Root != null) {
    Console.WriteLine($"{Maple2.File.Ingest.Helpers.ServerOverlay.Option}: {serverOverlay.Root}");
}

if (!File.Exists(exportedPath)) {
    throw new FileNotFoundException($"Could not find Exported.m2d file at path: {exportedPath}\n" +
                                    "Exported.m2d is in the Data/Resource folder of a MapleStory 2 client: MS2_DATA_FOLDER is (a copy of) the client's Data folder.");
}

if (!File.Exists(serverPath)) {
    throw new FileNotFoundException($"Could not find Server.m2d file at path: {serverPath}\n" + DataPack.Hint);
}

string? server = Environment.GetEnvironmentVariable("DB_IP");
string? port = Environment.GetEnvironmentVariable("DB_PORT");
string? database = Environment.GetEnvironmentVariable("DATA_DB_NAME");
string? user = Environment.GetEnvironmentVariable("DB_USER");
string? password = Environment.GetEnvironmentVariable("DB_PASSWORD");

if (server == null || port == null || database == null || user == null || password == null) {
    throw new ArgumentException("Database connection information was not set");
}

string worldServerDir = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.World");

bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

// check if dotnet ef is installed
Process processCheck;
if (isWindows) {
    processCheck = Process.Start("CMD.exe", "/C dotnet ef");
} else if (isLinux || isMac) {
    processCheck = Process.Start("bash", "-c \"dotnet ef\"");
} else {
    throw new PlatformNotSupportedException("Unsupported OS platform");
}
processCheck.WaitForExit();

if (processCheck.ExitCode != 0) {

    Process installEf;
    if (isWindows) {
        installEf = Process.Start("CMD.exe", "/C dotnet tool install --global dotnet-ef");
    } else if (isLinux || isMac) {
        installEf = Process.Start("bash", "-c \"dotnet tool install --global dotnet-ef\"");
    } else {
        throw new PlatformNotSupportedException("Unsupported OS platform");
    }
    installEf.WaitForExit();
    if (installEf.ExitCode != 0) {
        throw new Exception("Failed to install dotnet-ef. Please install it manually by running 'dotnet tool install --global dotnet-ef'");
    }

    if (isWindows) {
        string dotnetToolsPath = Environment.GetEnvironmentVariable("USERPROFILE") + "/.dotnet/tools";
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", currentPath + ";" + dotnetToolsPath);
        Console.WriteLine($"Updated PATH to include {dotnetToolsPath}");
    } else if (isLinux || isMac) {
        string dotnetToolsPath = Environment.GetEnvironmentVariable("HOME") + "/.dotnet/tools";
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", currentPath + ":" + dotnetToolsPath);
        Console.WriteLine($"Updated PATH to include {dotnetToolsPath}");
    } else {
        throw new PlatformNotSupportedException("Unsupported OS platform");
    }
}

string cmdCommand = "cd " + worldServerDir + " && dotnet ef database update";

Console.WriteLine("Migrating game database...");

Process process;
if (isWindows) {
    process = Process.Start("CMD.exe", "/C " + cmdCommand);
} else if (isLinux || isMac) {
    process = Process.Start("bash", "-c \"" + cmdCommand + "\"");
} else {
    throw new PlatformNotSupportedException("Unsupported OS platform");
}

process.WaitForExit();

if (process.ExitCode != 0) {
    throw new Exception("Failed to migrate game database.");
}

Console.WriteLine("Game Migration complete!");

using var xmlReader = new M2dReader(xmlPath);
using var exportedReader = new M2dReader(exportedPath);
using var serverReader = new M2dReader(serverPath);
using M2dReader? playedReader = playedXmlPath == null ? null : new M2dReader(playedXmlPath);

string dataDbConnection = $"Server={server};Port={port};Database={database};User={user};Password={password};oldguids=true";

DbContextOptions options = new DbContextOptionsBuilder()
    .UseMySql(dataDbConnection, ServerVersion.AutoDetect(dataDbConnection)).Options;

Console.WriteLine("Connecting to metadata database...");
using var metadataContext = new MetadataContext(options);

bool schemaChanged = SchemaVersionManager.ShouldRecreateDatabase(metadataContext);

if (dropData || schemaChanged) {
    Console.WriteLine("Dropping metadata database...");
    metadataContext.Database.EnsureDeleted();
    metadataContext.ChangeTracker.Clear();
}
Console.WriteLine("Ensuring metadata database is created...");
metadataContext.Database.EnsureCreated();
metadataContext.Database.ExecuteSqlRaw(@"SET GLOBAL max_allowed_packet=268435456"); // 256MB

// Store schema version after creation
SchemaVersionManager.StoreSchemaVersion(metadataContext);

Console.WriteLine("Starting data ingestion...");

// Filter Xml results based on feature settings.
Filter.Load(xmlReader, locale, env);
// The client turns off a feature with no level for the locale; the parser's filter keeps it (FeatureLevels).
FeatureLevels.Apply();
// With a played client, its own feature tables decide: the client reads feature.xml and feature_setting.xml from its
// pack (CFeatureManager::Init), and its levels differ from the data folder's (KR Live is 78 there, 56 here).
if (playedReader != null) {
    Dictionary<string, int>? playedFeatures = FeatureLevels.PlayedEnabled(playedReader, locale, env, out int playedLevel);
    if (playedFeatures == null) {
        Console.WriteLine($"The played client's pack has no {FeatureLevels.FeatureTable}; features follow the data folder's.");
    } else if (playedLevel == 0) {
        Console.Error.WriteLine($"The played client's {FeatureLevels.FeatureSettingTable} has no {env} level for {locale}, so that client turns every feature off. " +
                                "Run with the played client's LOCALE, or without MS2_PLAYED_CLIENT_XML.");
        Environment.Exit(1);
    } else {
        FeatureLocaleFilter.Features = playedFeatures;
        Console.WriteLine($"Features: the played client's tables at {env} {locale} = {playedLevel}, {playedFeatures.Count} on.");
    }
}
PackTables.Load(xmlReader, locale);
Console.WriteLine($"Rows filtered for {locale}; table/<region> files read from {PackTables.Locale}.");
if (playedReader != null && PackTables.PlayedLocaleWarning(PackTables.RegionFolders(playedReader.Files.Select(file => file.Name)), locale) is { } playedLocaleWarning) {
    Console.WriteLine(playedLocaleWarning);
}

// new TriggerGenerator(xmlReader).Generate();

var modelReaders = new List<PrefixedM2dReader> {
    new PrefixedM2dReader("/library/", Path.Combine(ms2Root, "Resource/Library.m2d")),
    new PrefixedM2dReader("/model/map/", Path.Combine(ms2Root, "Resource/Model/Map.m2d")),
    new PrefixedM2dReader("/model/effect/", Path.Combine(ms2Root, "Resource/Model/Effect.m2d")),
    new PrefixedM2dReader("/model/camera/", Path.Combine(ms2Root, "Resource/Model/Camera.m2d")),
    new PrefixedM2dReader("/model/tool/", Path.Combine(ms2Root, "Resource/Model/Tool.m2d")),
    new PrefixedM2dReader("/model/item/", Path.Combine(ms2Root, "Resource/Model/Item.m2d")),
    new PrefixedM2dReader("/model/npc/", Path.Combine(ms2Root, "Resource/Model/Npc.m2d")),
    new PrefixedM2dReader("/model/path/", Path.Combine(ms2Root, "Resource/Model/Path.m2d")),
    new PrefixedM2dReader("/model/character/", Path.Combine(ms2Root, "Resource/Model/Character.m2d")),
    new PrefixedM2dReader("/model/textures/", Path.Combine(ms2Root, "Resource/Model/Textures.m2d")),
};

UpdateDatabase(metadataContext, new TriggerMapper(xmlReader));

UpdateDatabase(metadataContext, new ItemMapper(xmlReader, language, false, playedReader));
UpdateDatabase(metadataContext, new NpcMapper(xmlReader, language, playedReader));

UpdateDatabase(metadataContext, new ServerTableMapper(serverReader, xmlReader, playedReader, originalDropTable, replayAttendGift, exportedReader, serverOverlay));
UpdateDatabase(metadataContext, new AiMapper(serverReader));

UpdateDatabase(metadataContext, new AdditionalEffectMapper(xmlReader, playedReader));
UpdateDatabase(metadataContext, new AnimationMapper(xmlReader));
UpdateDatabase(metadataContext, new PetMapper(xmlReader));
UpdateDatabase(metadataContext, new MapMapper(xmlReader, language, playedReader));
UpdateDatabase(metadataContext, new UgcMapMapper(xmlReader));
UpdateDatabase(metadataContext, new ExportedUgcMapMapper(xmlReader));
UpdateDatabase(metadataContext, new QuestMapper(xmlReader, language, playedReader));
UpdateDatabase(metadataContext, new RideMapper(xmlReader));
UpdateDatabase(metadataContext, new ScriptMapper(xmlReader, language, playedReader));
UpdateDatabase(metadataContext, new SkillMapper(xmlReader, language, playedReader));
UpdateDatabase(metadataContext, new TableMapper(xmlReader, language, playedReader, serverReader));
UpdateDatabase(metadataContext, new AchievementMapper(xmlReader, playedReader));
UpdateDatabase(metadataContext, new FunctionCubeMapper(xmlReader));
UpdateDatabase(metadataContext, new BanWordMapper(xmlReader));

NifParserHelper.ParseNif(modelReaders);

UpdateDatabase(metadataContext, new NifMapper());
UpdateDatabase(metadataContext, new NxsMeshMapper());

var index = new FlatTypeIndex(exportedReader);

var xBlockParser = new XBlockParser(exportedReader, index);

UpdateDatabase(metadataContext, new MapEntityMapper(metadataContext, xBlockParser));

var mapDataMapper = new MapDataMapper(metadataContext, xBlockParser);

UpdateDatabase(metadataContext, mapDataMapper);

mapDataMapper.ReportStats();

if (runNavmesh) {
    _ = new NavMeshMapper(metadataContext, exportedReader);
}

Console.WriteLine("Done!".ColorGreen());

void UpdateDatabase<T>(DbContext context, TypeMapper<T> mapper) where T : class {
    string? tableName = context.GetTableName<T>();
    Debug.Assert(!string.IsNullOrEmpty(tableName), $"Invalid table name: {tableName}");

    Console.Write($"Processing {tableName}... ");
    uint crc32C = mapper.Process();
    Console.Write($"Finished in {mapper.ElapsedMilliseconds}ms");
    Console.WriteLine();

    var checksum = context.Find<TableChecksum>(tableName);
    if (checksum != null) {
        if (checksum.Crc32C == crc32C) {
            Console.WriteLine($"Table {tableName} is up-to-date".ColorGreen());
            return;
        }

        checksum.Crc32C = crc32C;
        Console.WriteLine($"Table {tableName} outdated".ColorRed());
        int result = context.Database.ExecuteSqlRaw(@$"DELETE FROM `{tableName}`");
        Console.WriteLine($"Removed table {tableName} rows: {result}");
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    // Write entries to table
    foreach (T result in mapper.Results) {
        context.Add(result);
    }

    // Write checksum to table
    if (checksum == null) {
        context.Add(new TableChecksum {
            TableName = tableName,
            Crc32C = crc32C,
        });
    } else {
        context.Update(checksum);
    }

    context.SaveChanges();

    stopwatch.Stop();
    Console.WriteLine($"Wrote {mapper.Results.Count} entries to {tableName} in {stopwatch.ElapsedMilliseconds}ms");
}
