using System.Text.Json.Serialization;

namespace SchemaTools.Models;

public class PrimaryKeyConstraint
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("columns")]
  public List<string> Columns { get; set; } = new();

  [JsonPropertyName("isClustered")]
  public bool IsClustered { get; set; } = true;
}

public class ForeignKeyConstraint
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("columns")]
  public List<string> Columns { get; set; } = new();

  [JsonPropertyName("referencedTable")]
  public string ReferencedTable { get; set; } = string.Empty;

  [JsonPropertyName("referencedSchema")]
  public string? ReferencedSchema { get; set; }

  [JsonPropertyName("referencedColumns")]
  public List<string> ReferencedColumns { get; set; } = new();

  [JsonPropertyName("onDelete")]
  public string? OnDelete { get; set; }

  [JsonPropertyName("onUpdate")]
  public string? OnUpdate { get; set; }

  [JsonPropertyName("isComposite")]
  public bool IsComposite => Columns.Count > 1;
}

public class UniqueConstraint
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("columns")]
  public List<string> Columns { get; set; } = new();

  [JsonPropertyName("isClustered")]
  public bool IsClustered { get; set; }

  [JsonPropertyName("filterClause")]
  public string? FilterClause { get; set; }

  [JsonPropertyName("description")]
  public string? Description { get; set; }
}

public class CheckConstraint
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("expression")]
  public string Expression { get; set; } = string.Empty;

  [JsonPropertyName("description")]
  public string? Description { get; set; }
}

public class IndexMetadata
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("columns")]
  public List<IndexColumn> Columns { get; set; } = new();

  [JsonPropertyName("includedColumns")]
  public List<string>? IncludedColumns { get; set; }

  [JsonPropertyName("isUnique")]
  public bool IsUnique { get; set; }

  [JsonPropertyName("isClustered")]
  public bool IsClustered { get; set; }

  [JsonPropertyName("isColumnStore")]
  public bool IsColumnStore { get; set; }

  [JsonPropertyName("filterClause")]
  public string? FilterClause { get; set; }

  [JsonPropertyName("description")]
  public string? Description { get; set; }
}

public class IndexColumn
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("isDescending")]
  public bool IsDescending { get; set; }
}
