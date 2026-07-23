using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Catalog.Infrastructure.Persistence;

internal static class CatalogDbContextOptions
{
    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataSource);

        optionsBuilder.UseNpgsql(
            dataSource,
            ConfigureNpgsql);
    }

    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            connectionString);

        optionsBuilder.UseNpgsql(
            connectionString,
            ConfigureNpgsql);
    }

    private static void ConfigureNpgsql(
        NpgsqlDbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.MigrationsAssembly(
            typeof(CatalogDbContext)
                .Assembly
                .GetName()
                .Name);

        optionsBuilder.MigrationsHistoryTable(
            "__ef_migrations_history",
            CatalogDbContext.Schema);

        optionsBuilder.SetPostgresVersion(
            major: 18,
            minor: 0);
    }
}
