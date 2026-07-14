using System.Data.Common;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Tests for tiering the cold archive to an object store (s3://, gcs://, …). The S3 and GCS archive
///     round-trips against a live S3-compatible endpoint are gated on environment variables (see
///     <see cref="MinIoFactAttribute" />); the remote-purge guard runs everywhere because it never connects.
/// </summary>
public sealed class TieredStorageS3Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "duckdb-tier-s3-" + Guid.NewGuid().ToString("N"));

    public TieredStorageS3Tests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void Purge_on_a_remote_archive_is_rejected_with_guidance()
    {
        // No object-store access: the guard fires on the archive path's URL scheme before a connection opens.
        using var context = new RemotePurgeContext(
            Path.Combine(_root, "purge.duckdb"),
            "gcs://example-bucket/invoices");

        var ex = Assert.Throws<NotSupportedException>(() => context.Database.PurgeArchiveOlderThan<Invoice>(DateTime.UtcNow));
        Assert.Contains("lifecycle rule", ex.Message);
    }

    [MinIoFact]
    public Task Archives_and_reads_an_aggregate_on_s3()
        => ArchiveRoundTrip<S3Marker>("s3", "s3");

    [MinIoFact]
    public Task Archives_and_reads_an_aggregate_through_the_gcs_scheme()
        => ArchiveRoundTrip<GcsMarker>("gcs", "gcs");

    private async Task ArchiveRoundTrip<TMarker>(string scheme, string secretType)
    {
        var httpfs = HttpfsOptions.FromEnvironment(secretType)!;
        var archivePath = $"{scheme}://{httpfs.Bucket}/tier-int-{Guid.NewGuid():N}";

        using var context = new ObjectStoreContext<TMarker>(Path.Combine(_root, $"{scheme}.duckdb"), archivePath, httpfs);
        context.Database.EnsureCreated();

        var baseDate = new DateTime(2025, 7, 1);
        var id = 1;
        var lineId = 1;
        for (var m = 17; m >= 0; m--)
        {
            var invoice = new Invoice { Id = id++, InvoiceDate = baseDate.AddMonths(-m) };
            invoice.Lines.Add(new InvoiceLine { Id = lineId++, Amount = (m + 1) * 10 });
            context.Invoices.Add(invoice);
        }

        context.SaveChanges();
        var expected = (context.InvoiceHistory.Count(), context.LineHistory.Count(), context.LineHistory.Sum(l => l.Amount));

        var result = await context.Database.ArchiveTierAsync<Invoice>(baseDate.AddMonths(-12));

        Assert.Equal(5, result.RowsArchived);
        Assert.True(context.Invoices.Count() < 18);
        // The tiered read-models now read Parquet straight from the object store — full history, no dup/gap.
        Assert.Equal(expected, (context.InvoiceHistory.Count(), context.LineHistory.Count(), context.LineHistory.Sum(l => l.Amount)));

        // Idempotent re-run against the remote store.
        var rerun = await context.Database.ArchiveTierAsync<Invoice>(baseDate.AddMonths(-12));
        Assert.True(rerun.NoOp);
        Assert.Equal(expected, (context.InvoiceHistory.Count(), context.LineHistory.Count(), context.LineHistory.Sum(l => l.Amount)));
    }

    private sealed class Invoice { public int Id { get; set; } public DateTime InvoiceDate { get; set; } public List<InvoiceLine> Lines { get; set; } = []; }
    private sealed class InvoiceLine { public int Id { get; set; } public int InvoiceId { get; set; } public Invoice? Invoice { get; set; } public decimal Amount { get; set; } }
    private sealed class InvoiceRm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } }
    private sealed class InvoiceLineRm { public int Id { get; set; } public int InvoiceId { get; set; } public decimal Amount { get; set; } }

    private sealed class S3Marker { }
    private sealed class GcsMarker { }

    private sealed class ObjectStoreContext<TMarker>(string dbPath, string archivePath, HttpfsOptions httpfs) : DbContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceRm> InvoiceHistory => Set<InvoiceRm>();
        public DbSet<InvoiceLineRm> LineHistory => Set<InvoiceLineRm>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}");
            options.AddInterceptors(new HttpfsSetupInterceptor(httpfs));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices");
                b.HasMany(i => i.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLine>().ToTable("invoice_lines");
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceRm>()
                .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineRm>());
        }
    }

    private sealed class RemotePurgeContext(string dbPath, string archivePath) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month);
    }

    // Loads httpfs and configures object-store credentials on every connection open — the documented pattern.
    private sealed class HttpfsSetupInterceptor(HttpfsOptions options) : DbConnectionInterceptor
    {
        private string Sql =>
            $"INSTALL httpfs; LOAD httpfs; CREATE OR REPLACE SECRET objectstoretest (TYPE {options.SecretType}, "
            + $"KEY_ID {SqlLiteral(options.KeyId)}, SECRET {SqlLiteral(options.Secret)}, ENDPOINT {SqlLiteral(options.Endpoint)}, "
            + $"URL_STYLE 'path', USE_SSL {(options.UseSsl ? "true" : "false")})";

        private static string SqlLiteral(string value) => $"'{value.Replace("'", "''")}'";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed record HttpfsOptions(string SecretType, string Endpoint, string KeyId, string Secret, string Bucket, bool UseSsl)
    {
        public static HttpfsOptions? FromEnvironment(string secretType)
        {
            var endpoint = Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_ENDPOINT");
            var bucket = Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_BUCKET");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(bucket))
            {
                return null;
            }

            return new HttpfsOptions(
                secretType,
                endpoint,
                Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_KEY") ?? "minioadmin",
                Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_SECRET") ?? "minioadmin",
                bucket,
                string.Equals(Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_SSL"), "true", StringComparison.OrdinalIgnoreCase));
        }
    }
}

/// <summary>A <see cref="FactAttribute" /> that skips unless a live S3-compatible endpoint is configured.</summary>
public sealed class MinIoFactAttribute : FactAttribute
{
    public MinIoFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_ENDPOINT"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_BUCKET")))
        {
            Skip = "Set DUCKDB_S3_TEST_ENDPOINT and DUCKDB_S3_TEST_BUCKET (plus optional _KEY/_SECRET/_SSL) to run S3/GCS interoperability tests.";
        }
    }
}
