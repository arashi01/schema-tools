using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// SQL Server GENERATED ALWAYS column type for temporal tables.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GeneratedAlwaysType
{
  /// <summary>Period start column (GENERATED ALWAYS AS ROW START).</summary>
  RowStart = 1,

  /// <summary>Period end column (GENERATED ALWAYS AS ROW END).</summary>
  RowEnd = 2
}

public sealed record ColumnMetadata
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("type")]
  public required string Type { get; init; }

  /// <summary>
  /// Character or binary length (e.g., 100 for varchar(100)). Null for non-sized types or MAX columns.
  /// </summary>
  [JsonPropertyName("maxLength")]
  public int? MaxLength { get; init; }

  /// <summary>
  /// Numeric or temporal precision (e.g., 10 for decimal(10,2), 7 for datetime2(7)).
  /// Null for types without precision.
  /// </summary>
  [JsonPropertyName("precision")]
  public int? Precision { get; init; }

  /// <summary>
  /// Numeric scale (e.g., 2 for decimal(10,2)). Null for types without scale.
  /// </summary>
  [JsonPropertyName("scale")]
  public int? Scale { get; init; }

  /// <summary>
  /// True for MAX-length columns (varchar(max), nvarchar(max), varbinary(max)).
  /// </summary>
  [JsonPropertyName("isMaxLength")]
  public bool IsMaxLength { get; init; }

  [JsonPropertyName("nullable")]
  public bool Nullable { get; init; }

  [JsonPropertyName("isPrimaryKey")]
  public bool IsPrimaryKey { get; init; }

  [JsonPropertyName("isIdentity")]
  public bool IsIdentity { get; init; }

  [JsonPropertyName("isComputed")]
  public bool IsComputed { get; init; }

  [JsonPropertyName("computedExpression")]
  public string? ComputedExpression { get; init; }

  [JsonPropertyName("isPersisted")]
  public bool IsPersisted { get; init; }

  [JsonPropertyName("defaultValue")]
  public string? DefaultValue { get; init; }

  [JsonPropertyName("defaultConstraintName")]
  public string? DefaultConstraintName { get; init; }

  [JsonPropertyName("foreignKey")]
  public ForeignKeyReference? ForeignKey { get; init; }

  [JsonPropertyName("isPolymorphicForeignKey")]
  public bool IsPolymorphicForeignKey { get; init; }

  [JsonPropertyName("isCompositeFK")]
  public bool IsCompositeFK { get; init; }

  [JsonPropertyName("isUnique")]
  public bool IsUnique { get; init; }

  [JsonPropertyName("checkConstraint")]
  public string? CheckConstraint { get; init; }

  [JsonPropertyName("description")]
  public string? Description { get; init; }

  [JsonPropertyName("isGeneratedAlways")]
  public bool IsGeneratedAlways { get; init; }

  [JsonPropertyName("generatedAlwaysType")]
  public GeneratedAlwaysType? GeneratedAlwaysType { get; init; }
}

public sealed record ForeignKeyReference
{
  [JsonPropertyName("table")]
  public required string Table { get; init; }

  [JsonPropertyName("column")]
  public required string Column { get; init; }

  [JsonPropertyName("schema")]
  public string? Schema { get; init; }
}
