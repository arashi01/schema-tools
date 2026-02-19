using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// Referential action for foreign key ON DELETE / ON UPDATE clauses.
/// Values match the DacFx ForeignKeyAction naming convention.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ForeignKeyAction
{
  /// <summary>No action (SQL Server default).</summary>
  NoAction,

  /// <summary>CASCADE -- propagate to referencing rows.</summary>
  Cascade,

  /// <summary>SET NULL -- set referencing columns to NULL.</summary>
  SetNull,

  /// <summary>SET DEFAULT -- set referencing columns to their default value.</summary>
  SetDefault
}

public sealed record PrimaryKeyConstraint
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("columns")]
  public IReadOnlyList<string> Columns { get; init; } = [];

  [JsonPropertyName("isClustered")]
  public bool IsClustered { get; init; } = true;
}

public sealed record ForeignKeyConstraint
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("columns")]
  public IReadOnlyList<string> Columns { get; init; } = [];

  [JsonPropertyName("referencedTable")]
  public required string ReferencedTable { get; init; }

  [JsonPropertyName("referencedSchema")]
  public string? ReferencedSchema { get; init; }

  [JsonPropertyName("referencedColumns")]
  public IReadOnlyList<string> ReferencedColumns { get; init; } = [];

  [JsonPropertyName("onDelete")]
  public ForeignKeyAction OnDelete { get; init; } = ForeignKeyAction.NoAction;

  [JsonPropertyName("onUpdate")]
  public ForeignKeyAction OnUpdate { get; init; } = ForeignKeyAction.NoAction;

  [JsonPropertyName("isComposite")]
  public bool IsComposite => Columns.Count > 1;
}

public sealed record UniqueConstraint
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("columns")]
  public IReadOnlyList<string> Columns { get; init; } = [];

  [JsonPropertyName("isClustered")]
  public bool IsClustered { get; init; }

  [JsonPropertyName("filterClause")]
  public string? FilterClause { get; init; }

  [JsonPropertyName("description")]
  public string? Description { get; init; }
}

public sealed record CheckConstraint
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("expression")]
  public required string Expression { get; init; }

  [JsonPropertyName("description")]
  public string? Description { get; init; }
}

public sealed record IndexMetadata
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("columns")]
  public IReadOnlyList<IndexColumn> Columns { get; init; } = [];

  [JsonPropertyName("includedColumns")]
  public IReadOnlyList<string>? IncludedColumns { get; init; }

  [JsonPropertyName("isUnique")]
  public bool IsUnique { get; init; }

  [JsonPropertyName("isClustered")]
  public bool IsClustered { get; init; }

  [JsonPropertyName("isColumnStore")]
  public bool IsColumnStore { get; init; }

  [JsonPropertyName("filterClause")]
  public string? FilterClause { get; init; }

  [JsonPropertyName("description")]
  public string? Description { get; init; }
}

public sealed record IndexColumn
{
  [JsonPropertyName("name")]
  public required string Name { get; init; }

  [JsonPropertyName("isDescending")]
  public bool IsDescending { get; init; }
}
