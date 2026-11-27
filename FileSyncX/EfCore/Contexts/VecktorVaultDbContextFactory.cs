using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pgvector.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileSyncX.EfCore.Contexts;

/// <summary>
/// Fábrica usada em tempo de execução pelo DI e em tempo de design pelo EF Core.
/// </summary>
public sealed class VecktorVaultDbContextFactory :
    IDesignTimeDbContextFactory<VecktorVaultDbContext>,
    IDbContextFactory<VecktorVaultDbContext>
{
    public VecktorVaultDbContext CreateDbContext()
        => CreateDbContext(Array.Empty<string>());

    public Task<VecktorVaultDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }

    public VecktorVaultDbContext CreateDbContext(string[] args)
    {
        string[] possiblePaths =
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        string? basePath = possiblePaths.FirstOrDefault(path =>
            File.Exists(Path.Combine(path, "appsettings.json")));

        if (basePath is null)
        {
            throw new FileNotFoundException(
                "Não foi possível localizar appsettings.json. Verifique se o arquivo está no diretório do projeto ou de execução.");
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        string? connectionString = configuration.GetConnectionString("VecktorVault");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string 'VecktorVault' não foi encontrada em appsettings.json.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<VecktorVaultDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector());

        return new VecktorVaultDbContext(optionsBuilder.Options);
    }
}
