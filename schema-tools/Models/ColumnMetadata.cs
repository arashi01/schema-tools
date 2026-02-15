using System.Text.Json.Serialization;

namespace SchemaTools.Models;

public class ColumnMetadata
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("type")]
  public string Type { get; set; } = string.Empty;

  /// <summary>
  /// Character or binary length (e.g., 100 for varchar(100)). Null for non-sized types or MAX columns.
  /// </summary>
  [JsonPropertyName("maxLength")]
  public int? MaxLength { get; set; }

  /// <summary>
  /// Numeric or temporal precision (e.g., 10 for decimal(10,2), 7 for datetime2(7)).
  /// Null for types without precision.
  /// </summary>
  [JsonPropertyName("precision")]
  public int? Precision { get; set; }

  /// <summary>
  /// Numeric scale (e.g., 2 for decimal(10,2)). Null for types without scale.
  /// </summary>
  [JsonPropertyName("scale")]
  public int? Scale { get; set; }

  /// <summary>
  /// True for MAX-length columns (varchar(max), nvarchar(max), varbinary(max)).
  /// </summary>
  [JsonPropertyName("isMaxLength")]
  public bool IsMaxLength { get; set; }

  [JsonPropertyName("nullable")]
  public bool Nullable { get; set; }

  [JsonPropertyName("isPrimaryKey")]
  public bool IsPrimaryKey { get; set; }

  [JsonPropertyName("isIdentity")]
  public bool IsIdentity { get; set; }

  [JsonPropertyName("isComputed")]
  public bool IsComputed { get; set; }

  [JsonPropertyName("computedExpression")]
  public string? ComputedExpression { get; set; }

  [JsonPropertyName("isPersisted")]
  public bool IsPersisted { get; set; }

  [JsonPropertyName("defaultValue")]
  public string? DefaultValue { get; set; }

  [JsonPropertyName("defaultConstraintName")]
  public string? DefaultConstraintName { get; set; }

  [JsonPropertyName("foreignKey")]
  public ForeignKeyReference? ForeignKey { get; set; }

  [JsonPropertyName("isPolymorphicForeignKey")]
  public bool IsPolymorphicForeignKey { get; set; }

  [JsonPropertyName("isCompositeFK")]
  public bool IsCompositeFK { get; set; }

  [JsonPropertyName("isUnique")]
  public bool IsUnique { get; set; }

  [JsonPropertyName("checkConstraint")]
  public string? CheckConstraint { get; set; }

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("isGeneratedAlways")]
  public bool IsGeneratedAlways { get; set; }

  [JsonPropertyName("generatedAlwaysType")]
  public string? GeneratedAlwaysType { get; set; }
}

public class ForeignKeyReference
{
  [JsonPropertyName("table")]
  public string Table { get; set; } = string.Empty;

  [JsonPropertyName("column")]
  public string Column { get; set; } = string.Empty;

  [JsonPropertyName("schema")]
  public string? Schema { get; set; }
}
