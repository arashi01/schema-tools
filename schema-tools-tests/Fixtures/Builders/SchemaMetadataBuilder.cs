using SchemaTools.Models;

namespace SchemaTools.Tests.Fixtures.Builders;

/// <summary>
/// Fluent builder for <see cref="SchemaMetadata"/> test instances.
/// Uses <c>with</c> expressions to preserve record immutability.
/// </summary>
internal sealed class SchemaMetadataBuilder
{
  private SchemaMetadata _metadata;

  public SchemaMetadataBuilder()
  {
    _metadata = new SchemaMetadata
    {
      Database = "TestDB",
      DefaultSchema = "test",
      GeneratedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
  }

  public SchemaMetadataBuilder WithDatabase(string database)
  {
    _metadata = _metadata with { Database = database };
    return this;
  }

  public SchemaMetadataBuilder WithDefaultSchema(string schema)
  {
    _metadata = _metadata with { DefaultSchema = schema };
    return this;
  }

  public SchemaMetadataBuilder WithTable(TableMetadata table)
  {
    _metadata = _metadata with { Tables = [.. _metadata.Tables, table] };
    return this;
  }

  public SchemaMetadataBuilder WithTables(params TableMetadata[] tables)
  {
    _metadata = _metadata with { Tables = [.. tables] };
    return this;
  }

  public SchemaMetadataBuilder WithCategories(Dictionary<string, string> categories)
  {
    _metadata = _metadata with { Categories = categories };
    return this;
  }

  public SchemaMetadataBuilder WithStatistics(SchemaStatistics statistics)
  {
    _metadata = _metadata with { Statistics = statistics };
    return this;
  }

  public SchemaMetadataBuilder WithStatistics(
    int totalTables = 0,
    int temporalTables = 0,
    int softDeleteTables = 0,
    int appendOnlyTables = 0,
    int totalColumns = 0,
    int totalConstraints = 0)
  {
    _metadata = _metadata with
    {
      Statistics = new SchemaStatistics
      {
        TotalTables = totalTables,
        TemporalTables = temporalTables,
        SoftDeleteTables = softDeleteTables,
        AppendOnlyTables = appendOnlyTables,
        TotalColumns = totalColumns,
        TotalConstraints = totalConstraints
      }
    };
    return this;
  }

  public SchemaMetadataBuilder Configure(Func<SchemaMetadata, SchemaMetadata> configure)
  {
    _metadata = configure(_metadata);
    return this;
  }

  public SchemaMetadata Build() => _metadata;
}
