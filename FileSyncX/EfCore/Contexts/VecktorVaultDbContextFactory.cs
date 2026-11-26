
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

using Pgvector.EntityFrameworkCore;

namespace FileSyncX.EfCore.Contexts;

public sealed class VecktorVaultDbContextFactory
    : IDesignTimeDbContextFactory<VecktorVaultDbContext>
{
    public VecktorVaultDbContext CreateDbContext(string[] args)
    {
        // Procura appsettings.json no diretório do projeto
        // ou no diretório de execução.

        var possiblePaths = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        var basePath = possiblePaths.FirstOrDefault(
            path => File.Exists(
                Path.Combine(path, "appsettings.json")));

        if (basePath is null)
        {
            throw new FileNotFoundException(
                "Não foi possível localizar appsettings.json.");
        }

        // Carrega configurações
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile(
                "appsettings.json",
                optional: false)
            .Build();

        var connectionString = configuration
            .GetConnectionString("VecktorVault");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string VecktorVault não encontrada.");
        }

        // Configura DbContext
        var optionsBuilder =
            new DbContextOptionsBuilder<VecktorVaultDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
            });

        return new VecktorVaultDbContext(
            optionsBuilder.Options);
    }
}
