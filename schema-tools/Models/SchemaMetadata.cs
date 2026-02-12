using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// Complete schema metadata for the entire database
/// </summary>
public class SchemaMetadata
{
  [JsonPropertyName("$schema")]
  public string Schema { get; set; } = "./schema-metadata.schema.json";

  [JsonPropertyName("version")]
  public string Version { get; set; } = "1.0.0";

  [JsonPropertyName("generatedAt")]
  public DateTime GeneratedAt { get; set; }

  [JsonPropertyName("generatedBy")]
  public string GeneratedBy { get; set; } = "SchemaMetadataGenerator";

  [JsonPropertyName("database")]
  public string Database { get; set; } = "Database";

  [JsonPropertyName("defaultSchema")]
  public string DefaultSchema { get; set; } = "dbo";

  [JsonPropertyName("sqlServerVersion")]
  public string SqlServerVersion { get; set; } = "Sql160";

  [JsonPropertyName("tables")]
  public List<TableMetadata> Tables { get; set; } = new();

  [JsonPropertyName("statistics")]
  public SchemaStatistics Statistics { get; set; } = new();

  [JsonPropertyName("categories")]
  public Dictionary<string, string> Categories { get; set; } = new();
}

public class SchemaStatistics
{
  [JsonPropertyName("totalTables")]
  public int TotalTables { get; set; }

  [JsonPropertyName("temporalTables")]
  public int TemporalTables { get; set; }

  [JsonPropertyName("softDeleteTables")]
  public int SoftDeleteTables { get; set; }

  [JsonPropertyName("appendOnlyTables")]
  public int AppendOnlyTables { get; set; }

  [JsonPropertyName("polymorphicTables")]
  public int PolymorphicTables { get; set; }

  [JsonPropertyName("triggersToGenerate")]
  public int TriggersToGenerate { get; set; }

  [JsonPropertyName("totalColumns")]
  public int TotalColumns { get; set; }

  [JsonPropertyName("totalConstraints")]
  public int TotalConstraints { get; set; }
}
