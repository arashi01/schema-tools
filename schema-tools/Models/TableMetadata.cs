using System.Text.Json.Serialization;

namespace SchemaTools.Models;

public sealed record TableMetadata
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("schema")]
  public string Schema { get; init; } = SchemaToolsDefaults.DefaultSchema;

  [JsonPropertyName("category")]
  public string? Category { get; init; }

  [JsonPropertyName("description")]
  public string? Description { get; init; }

  [JsonPropertyName("hasTemporalVersioning")]
  public bool HasTemporalVersioning { get; init; }

  [JsonPropertyName("hasActiveColumn")]
  public bool HasActiveColumn { get; init; }

  [JsonPropertyName("hasSoftDelete")]
  public bool HasSoftDelete { get; init; }

  /// <summary>
  /// The name of the active column for soft-delete tables (e.g., "active", "is_enabled").
  /// Only populated when HasActiveColumn is true.
  /// </summary>
  [JsonPropertyName("activeColumnName")]
  public string? ActiveColumnName { get; init; }

  [JsonPropertyName("isAppendOnly")]
  public bool IsAppendOnly { get; init; }

  [JsonPropertyName("isPolymorphic")]
  public bool IsPolymorphic { get; init; }

  [JsonPropertyName("polymorphicOwner")]
  public PolymorphicOwnerInfo? PolymorphicOwner { get; init; }

  [JsonPropertyName("primaryKey")]
  public string? PrimaryKey { get; init; }

  [JsonPropertyName("primaryKeyType")]
  public string? PrimaryKeyType { get; init; }

  [JsonPropertyName("historyTable")]
  public string? HistoryTable { get; init; }

  /// <summary>
  /// True if this table is a temporal history table (referenced by another table's HistoryTable property).
  /// History tables do not have primary keys by design and are excluded from certain validations.
  /// </summary>
  [JsonPropertyName("isHistoryTable")]
  public bool IsHistoryTable { get; init; }

  [JsonPropertyName("columns")]
  public IReadOnlyList<ColumnMetadata> Columns { get; init; } = [];

  [JsonPropertyName("constraints")]
  public ConstraintsCollection Constraints { get; init; } = new();

  [JsonPropertyName("indexes")]
  public IReadOnlyList<IndexMetadata> Indexes { get; init; } = [];
}

public sealed record PolymorphicOwnerInfo
{
  [JsonPropertyName("typeColumn")]
  public required string TypeColumn { get; init; }

  [JsonPropertyName("idColumn")]
  public required string IdColumn { get; init; }

  [JsonPropertyName("allowedTypes")]
  public IReadOnlyList<string> AllowedTypes { get; init; } = [];
}

public sealed record ConstraintsCollection
{
  [JsonPropertyName("primaryKey")]
  public PrimaryKeyConstraint? PrimaryKey { get; init; }

  [JsonPropertyName("foreignKeys")]
  public IReadOnlyList<ForeignKeyConstraint> ForeignKeys { get; init; } = [];

  [JsonPropertyName("uniqueConstraints")]
  public IReadOnlyList<UniqueConstraint> UniqueConstraints { get; init; } = [];

  [JsonPropertyName("checkConstraints")]
  public IReadOnlyList<CheckConstraint> CheckConstraints { get; init; } = [];
}
