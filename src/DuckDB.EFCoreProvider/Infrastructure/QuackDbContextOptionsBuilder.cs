using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Infrastructure;

/// <summary>Configures the provider's experimental Quack remote profile.</summary>
/// <remarks>
///     Quack is experimental in DuckDB 1.5.x. This builder is only created by an explicit
///     <c>UseQuack</c> call; existing in-process DuckDB and DuckLake profiles are unaffected.
/// </remarks>
public sealed class QuackDbContextOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    internal QuackDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        => _optionsBuilder = optionsBuilder;

    /// <summary>Uses plain HTTP instead of HTTPS for the Quack transport.</summary>
    /// <remarks>Only use this for loopback endpoints or a network protected by another secure transport.</remarks>
    public QuackDbContextOptionsBuilder DisableSsl(bool disable = true)
        => WithOption(options => options with { DisableSsl = disable });

    /// <summary>Sets the private local catalog alias used to hold the remote Quack session.</summary>
    public QuackDbContextOptionsBuilder CatalogName(string catalogName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        DuckLakeDbContextOptionsBuilder.ValidateIdentifier(catalogName, nameof(catalogName), "Quack catalog");
        return WithOption(options => options with { CatalogName = catalogName });
    }

    /// <summary>Controls reuse of HTTP connections across remote requests. Enabled by default.</summary>
    public QuackDbContextOptionsBuilder EnableHttpConnectionCaching(bool enable = true)
        => WithOption(options => options with { EnableHttpConnectionCaching = enable });

    /// <summary>Controls how the client-side Quack extension is provisioned.</summary>
    public QuackDbContextOptionsBuilder ExtensionLoadMode(DuckDBExtensionLoadMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return WithOption(options => options with { ExtensionLoadMode = mode });
    }

    /// <summary>Loads Quack from an explicit extension file, useful for pinned or offline deployments.</summary>
    public QuackDbContextOptionsBuilder ExtensionPath(string extensionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionPath);
        return WithOption(options => options with
        {
            ExtensionPath = extensionPath,
            ExtensionLoadMode = DuckDBExtensionLoadMode.LoadOnly
        });
    }

    private QuackDbContextOptionsBuilder WithOption(Func<QuackOptions, QuackOptions> setAction)
    {
        var infrastructure = (IDbContextOptionsBuilderInfrastructure)_optionsBuilder;
        var extension = _optionsBuilder.Options.FindExtension<DuckDBOptionsExtension>()
            ?? throw new InvalidOperationException("Configure DuckDB before configuring Quack.");
        var options = extension.QuackOptions
            ?? throw new InvalidOperationException("Configure the Quack endpoint before changing its options.");
        infrastructure.AddOrUpdateExtension(extension.WithQuackOptions(setAction(options)));
        return this;
    }
}