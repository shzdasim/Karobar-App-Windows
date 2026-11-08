using Karobar.Application.Interfaces;
using Karobar.Infrastructure.Importing;
using Karobar.Infrastructure.Persistence;
using Karobar.Infrastructure.Security;
using Karobar.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Karobar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKarobarInfrastructure(
        this IServiceCollection services,
        string? customDataDir = null)
    {
        var baseDir = !string.IsNullOrWhiteSpace(customDataDir)
            ? customDataDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karobar");

        var dataDir = Path.Combine(baseDir, "Data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "karobar.db");

        services.AddDbContextFactory<KarobarDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath};Cache=Shared");
        });

        services.AddSingleton<IDatabaseInitializerService>(sp =>
            new KarobarDatabaseInitializer(
                sp.GetRequiredService<IDbContextFactory<KarobarDbContext>>(),
                sp.GetRequiredService<ILogger<KarobarDatabaseInitializer>>(),
                customDataDir));

        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ILedgerRebuildService, LedgerRebuildService>();
        services.AddSingleton<ILegacyKarobarImporterService, LegacyKarobarImporterService>();

        return services;
    }
}
