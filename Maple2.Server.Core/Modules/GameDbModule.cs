using System.Reflection;
using Autofac;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Microsoft.EntityFrameworkCore;
using Module = Autofac.Module;

namespace Maple2.Server.Core.Modules;

public class GameDbModule : Module {
    private const string NAME = "GameDbOptions";

    private readonly DbContextOptions options;

    public GameDbModule() {
        string? server = Environment.GetEnvironmentVariable("DB_IP");
        string? port = Environment.GetEnvironmentVariable("DB_PORT");
        string? database = Environment.GetEnvironmentVariable("GAME_DB_NAME");
        string? user = Environment.GetEnvironmentVariable("DB_USER");
        string? password = Environment.GetEnvironmentVariable("DB_PASSWORD");

        if (server == null || port == null || database == null || user == null || password == null) {
            throw new ArgumentException("Database connection information was not set");
        }

        string gameDbConnection = $"Server={server};Port={port};Database={database};User={user};Password={password};oldguids=true";

        options = Options(gameDbConnection);
    }

    // The game database's options, for every server: each connection runs in the server's local time zone.
    public static DbContextOptions Options(string connection) {
        return new DbContextOptionsBuilder()
            .UseMySql(connection, ServerVersion.AutoDetect(connection))
            .AddInterceptors(LocalTimeZoneInterceptor.Instance)
            .Options;
    }

    protected override void Load(ContainerBuilder builder) {
        builder.RegisterInstance(options)
            .Named<DbContextOptions>(NAME);

        builder.RegisterType<GameStorage>()
            .WithParameter(Condition, Resolve)
            .SingleInstance();
    }

    private static bool Condition(ParameterInfo info, IComponentContext context) {
        return info.Name == "options";
    }

    private static DbContextOptions Resolve(ParameterInfo info, IComponentContext context) {
        return context.ResolveNamed<DbContextOptions>(NAME);
    }
}
