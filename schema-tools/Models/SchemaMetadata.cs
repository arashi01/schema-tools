using System.Text.Json.Serialization;

namespace SchemaTools.Models;

public sealed record SchemaMetadata
{
  [JsonPropertyName("$schema")]
  public string Schema { get; init; } = "./schema.schema.json";

  [JsonPropertyName("version")]
  public string Version { get; init; } = SchemaToolsDefaults.MetadataVersion;

  [JsonPropertyName("generatedAt")]
  public DateTime GeneratedAt { get; init; }

  [JsonPropertyName("generatedBy")]
  public string GeneratedBy { get; init; } = "SchemaMetadataExtractor";

  [JsonPropertyName("database")]
  public string Database { get; init; } = "Database";

  [JsonPropertyName("defaultSchema")]
  public string DefaultSchema { get; init; } = SchemaToolsDefaults.DefaultSchema;

  [JsonPropertyName("sqlServerVersion")]
  public SqlServerVersion SqlServerVersion { get; init; } = SqlServerVersion.Sql170;

  [JsonPropertyName("tables")]
  public IReadOnlyList<TableMetadata> Tables { get; init; } = [];

  [JsonPropertyName("statistics")]
  public SchemaStatistics Statistics { get; init; } = new();

  [JsonPropertyName("categories")]
  public IReadOnlyDictionary<string, string> Categories { get; init; } = new Dictionary<string, string>();
}

public sealed record SchemaStatistics
{
  [JsonPropertyName("totalTables")]
  public int TotalTables { get; init; }

  [JsonPropertyName("temporalTables")]
  public int TemporalTables { get; init; }

  [JsonPropertyName("softDeleteTables")]
  public int SoftDeleteTables { get; init; }

  [JsonPropertyName("appendOnlyTables")]
  public int AppendOnlyTables { get; init; }

  [JsonPropertyName("polymorphicTables")]
  public int PolymorphicTables { get; init; }

  [JsonPropertyName("totalColumns")]
  public int TotalColumns { get; init; }

  [JsonPropertyName("totalConstraints")]
  public int TotalConstraints { get; init; }
}
