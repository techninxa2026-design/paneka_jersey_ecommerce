using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Jersey_ecommerce.Data;

/// <summary>
/// EF tooling reads configuration without executing Program.cs, identity role
/// seeding, or any live website startup operations.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var projectRoot = File.Exists(Path.Combine(root, "appsettings.json"))
            ? root : Path.Combine(root, "Jersey-ecommerce");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "Set ConnectionStrings:DefaultConnection or the ConnectionStrings__DefaultConnection environment variable before running EF database commands.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;
        return new ApplicationDbContext(options);
    }
}
