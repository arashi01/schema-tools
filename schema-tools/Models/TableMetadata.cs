using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// Metadata for a single table
/// </summary>
public class TableMetadata
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("schema")]
  public string Schema { get; set; } = "dbo";

  [JsonPropertyName("category")]
  public string? Category { get; set; }

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("hasTemporalVersioning")]
  public bool HasTemporalVersioning { get; set; }

  [JsonPropertyName("hasActiveColumn")]
  public bool HasActiveColumn { get; set; }

  [JsonPropertyName("hasSoftDelete")]
  public bool HasSoftDelete { get; set; }

  [JsonPropertyName("isAppendOnly")]
  public bool IsAppendOnly { get; set; }

  [JsonPropertyName("isPolymorphic")]
  public bool IsPolymorphic { get; set; }

  [JsonPropertyName("polymorphicOwner")]
  public PolymorphicOwnerInfo? PolymorphicOwner { get; set; }

  [JsonPropertyName("primaryKey")]
  public string? PrimaryKey { get; set; }

  [JsonPropertyName("primaryKeyType")]
  public string? PrimaryKeyType { get; set; }

  [JsonPropertyName("historyTable")]
  public string? HistoryTable { get; set; }

  [JsonPropertyName("columns")]
  public List<ColumnMetadata> Columns { get; set; } = new();

  [JsonPropertyName("constraints")]
  public ConstraintsCollection Constraints { get; set; } = new();

  [JsonPropertyName("indexes")]
  public List<IndexMetadata> Indexes { get; set; } = new();

  [JsonPropertyName("triggers")]
  public TriggerConfiguration Triggers { get; set; } = new();
}

public class PolymorphicOwnerInfo
{
  [JsonPropertyName("typeColumn")]
  public string TypeColumn { get; set; } = string.Empty;

  [JsonPropertyName("idColumn")]
  public string IdColumn { get; set; } = string.Empty;

  [JsonPropertyName("allowedTypes")]
  public List<string> AllowedTypes { get; set; } = new();
}

public class ConstraintsCollection
{
  [JsonPropertyName("primaryKey")]
  public PrimaryKeyConstraint? PrimaryKey { get; set; }

  [JsonPropertyName("foreignKeys")]
  public List<ForeignKeyConstraint> ForeignKeys { get; set; } = new();

  [JsonPropertyName("uniqueConstraints")]
  public List<UniqueConstraint> UniqueConstraints { get; set; } = new();

  [JsonPropertyName("checkConstraints")]
  public List<CheckConstraint> CheckConstraints { get; set; } = new();
}

public class TriggerConfiguration
{
  [JsonPropertyName("hardDelete")]
  public HardDeleteTrigger HardDelete { get; set; } = new();

  [JsonPropertyName("custom")]
  public List<CustomTrigger> Custom { get; set; } = new();
}

public class HardDeleteTrigger
{
  [JsonPropertyName("generate")]
  public bool Generate { get; set; }

  [JsonPropertyName("name")]
  public string? Name { get; set; }

  [JsonPropertyName("activeColumnName")]
  public string ActiveColumnName { get; set; } = "active";

  [JsonPropertyName("reason")]
  public string? Reason { get; set; }
}

public class CustomTrigger
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("timing")]
  public string? Timing { get; set; }

  [JsonPropertyName("events")]
  public List<string>? Events { get; set; }
}
