using System.Text.Json.Serialization;

namespace SchemaTools.Models;

public class TableMetadata
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("schema")]
  public string Schema { get; set; } = SchemaToolsDefaults.DefaultSchema;

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

  /// <summary>
  /// The name of the active column for soft-delete tables (e.g., "active", "is_enabled").
  /// Only populated when HasActiveColumn is true.
  /// </summary>
  [JsonPropertyName("activeColumnName")]
  public string? ActiveColumnName { get; set; }

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

  /// <summary>
  /// True if this table is a temporal history table (referenced by another table's HistoryTable property).
  /// History tables do not have primary keys by design and are excluded from certain validations.
  /// </summary>
  [JsonPropertyName("isHistoryTable")]
  public bool IsHistoryTable { get; set; }

  [JsonPropertyName("columns")]
  public List<ColumnMetadata> Columns { get; set; } = new();

  [JsonPropertyName("constraints")]
  public ConstraintsCollection Constraints { get; set; } = new();

  [JsonPropertyName("indexes")]
  public List<IndexMetadata> Indexes { get; set; } = new();
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
