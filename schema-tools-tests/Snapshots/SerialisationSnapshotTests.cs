using System.Text.Json;
using System.Text.Json.Serialization;
using SchemaTools.Models;
using SchemaTools.Tests.Fixtures.Builders;

namespace SchemaTools.Tests.Snapshots;

/// <summary>
/// Verify snapshot tests for JSON serialisation of core model types.
/// Guards against accidental changes to the JSON contract consumed by
/// downstream generators and external tooling.
/// </summary>
public class SerialisationSnapshotTests
{
  private static readonly JsonSerializerOptions SerialiseOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  [Fact]
  public Task SchemaMetadata_Serialisation_MatchesSnapshot()
  {
    SchemaMetadata metadata = new SchemaMetadataBuilder()
      .WithDatabase("SnapshotTestDB")
      .WithDefaultSchema("dbo")
      .WithCategories(new Dictionary<string, string>
      {
        ["core"] = "Core identity tables",
        ["audit"] = "Audit trail tables"
      })
      .WithTable(new TableMetadataBuilder("users")
        .WithSchema("dbo")
        .WithCategory("core")
        .WithDescription("Stores user accounts")
        .WithSoftDelete()
        .WithTemporalVersioning("[dbo].[users_history]")
        .WithPrimaryKey("id", "UNIQUEIDENTIFIER")
        .WithColumn("email", "NVARCHAR", false, c => c with
        {
          MaxLength = 256,
          IsUnique = true
        })
        .WithForeignKey("tenant_id", "tenants")
        .Build())
      .WithTable(new TableMetadataBuilder("audit_log")
        .WithSchema("dbo")
        .WithCategory("audit")
        .AsAppendOnly()
        .WithPrimaryKey("id", "BIGINT")
        .Build())
      .WithStatistics(
        totalTables: 2,
        temporalTables: 1,
        softDeleteTables: 1,
        appendOnlyTables: 1,
        totalColumns: 8,
        totalConstraints: 3)
      .Configure(m => m with
      {
        Version = "1.0.0-snapshot",
        GeneratedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        GeneratedBy = "SnapshotTest"
      })
      .Build();

    string json = JsonSerializer.Serialize(metadata, SerialiseOptions);
    return Verifier.VerifyJson(json);
  }

  [Fact]
  public Task SourceAnalysisResult_Serialisation_MatchesSnapshot()
  {
    SourceAnalysisResult analysis = new SourceAnalysisResultBuilder()
      .WithDefaultSchema("dbo")
      .WithTable(new TableAnalysisBuilder("users")
        .WithSchema("dbo")
        .WithCategory("core")
        .WithDescription("Core user accounts with temporal versioning.")
        .WithSourceFile("Tables/users.sql")
        .WithSoftDelete(SoftDeleteMode.Cascade)
        .WithTemporalVersioning("[dbo].[users_history]")
        .WithPrimaryKey("id")
        .WithForeignKeyTo("tenants", "tenant_id")
        .WithChildTables("orders", "user_preferences")
        .Build())
      .WithTable(new TableAnalysisBuilder("orders")
        .WithSchema("dbo")
        .WithCategory("core")
        .WithDescription("Customer orders linked to users.")
        .WithSourceFile("Tables/orders.sql")
        .WithSoftDelete(SoftDeleteMode.Restrict)
        .WithPrimaryKey("id")
        .WithForeignKeyTo("users", "user_id")
        .AsLeafTable()
        .Build())
      .WithExistingTrigger(
        "trg_users_soft_delete",
        "users",
        "_generated/Triggers/trg_users_soft_delete.sql",
        isGenerated: true,
        schema: "dbo")
      .WithExistingView(
        "vw_users",
        "_generated/Views/vw_users.sql",
        isGenerated: true,
        schema: "dbo")
      .WithGeneratedDirectories(
        "_generated/Triggers",
        "_generated/Views")
      .Configure(a => a with
      {
        Version = "1.0.0-snapshot",
        AnalysedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
      })
      .Build();

    string json = JsonSerializer.Serialize(analysis, SerialiseOptions);
    return Verifier.VerifyJson(json);
  }
}
