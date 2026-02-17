using System.Text;
using SchemaTools.Configuration;
using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Generates comprehensive Markdown documentation from schema metadata
/// </summary>
public class MarkdownDocumentationGenerator : MSTask
{
  [Microsoft.Build.Framework.Required]
  public string MetadataFile { get; set; } = string.Empty;

  [Microsoft.Build.Framework.Required]
  public string OutputFile { get; set; } = string.Empty;

  public string ConfigFile { get; set; } = string.Empty;

  public bool? IncludeErDiagrams { get; set; }
  public bool? IncludeStatistics { get; set; }
  public bool? IncludeConstraints { get; set; }
  public bool? IncludeIndexes { get; set; }

  internal SchemaToolsConfig? TestConfig { get; set; }
  internal SchemaMetadata? TestMetadata { get; set; }

  private SchemaToolsConfig _config = new();

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "  Documentation Generator");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      LoadConfiguration();

      SchemaMetadata? metadata = LoadMetadata();
      if (metadata == null)
      {
        return false;
      }

      Log.LogMessage($"Generating documentation for {metadata.Tables.Count} tables...");

      var markdown = new StringBuilder();

      markdown.AppendLine("# Database Schema Documentation");
      markdown.AppendLine();
      markdown.AppendLine($"**Database:** {metadata.Database}  ");
      markdown.AppendLine($"**Generated:** {metadata.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC  ");
      markdown.AppendLine($"**Schema Version:** {metadata.Version}  ");
      markdown.AppendLine($"**SQL Server:** {metadata.SqlServerVersion}");
      markdown.AppendLine();

      GenerateTableOfContents(markdown, metadata);

      if (GetDocSetting(IncludeStatistics, _config.Documentation.IncludeStatistics))
      {
        GenerateStatistics(markdown, metadata);
      }

      if (GetDocSetting(IncludeErDiagrams, _config.Documentation.IncludeErDiagrams))
      {
        GenerateErDiagrams(markdown, metadata);
      }

      GenerateTableDocumentation(markdown, metadata);

      GenerationUtilities.EnsureDirectoryExists(OutputFile);
      File.WriteAllText(OutputFile, markdown.ToString(), Encoding.UTF8);

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"+ Documentation written to: {OutputFile}");
      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Failed to generate documentation: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private void LoadConfiguration()
  {
    _config = ConfigurationLoader.Load(ConfigFile, TestConfig);
  }

  private SchemaMetadata? LoadMetadata()
  {
    OperationResult<SchemaMetadata> metadataResult = MetadataLoader.Load(MetadataFile, TestMetadata);

    if (!metadataResult.IsSuccess)
    {
      DiagnosticReporter.Report(Log, metadataResult.Diagnostics);
      return null;
    }

    return metadataResult.Value;
  }

  private static bool GetDocSetting(bool? taskParameter, bool configValue)
  {
    return taskParameter ?? configValue;
  }

  private static void GenerateTableOfContents(StringBuilder markdown, SchemaMetadata metadata)
  {
    markdown.AppendLine("## Table of Contents");
    markdown.AppendLine();
    markdown.AppendLine("- [Statistics](#statistics)");
    markdown.AppendLine("- [Entity Relationship Diagrams](#entity-relationship-diagrams)");

    foreach (IGrouping<string, TableMetadata>? category in metadata.Tables.GroupBy(t => t.Category ?? "Uncategorised").OrderBy(g => g.Key))
    {
      string categorySlug = category.Key.ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
      markdown.AppendLine($"- [{category.Key}](#{categorySlug})");

      foreach (TableMetadata? table in category.OrderBy(t => t.Name))
      {
        string tableSlug = table.Name.ToLowerInvariant();
        markdown.AppendLine($"  - [{table.Name}](#{tableSlug})");
      }
    }

    markdown.AppendLine();
  }

  private static void GenerateStatistics(StringBuilder markdown, SchemaMetadata metadata)
  {
    markdown.AppendLine("## Statistics");
    markdown.AppendLine();
    markdown.AppendLine("| Metric | Count |");
    markdown.AppendLine("|--------|-------|");
    markdown.AppendLine($"| Total Tables | {metadata.Statistics.TotalTables} |");
    markdown.AppendLine($"| Temporal Tables | {metadata.Statistics.TemporalTables} |");
    markdown.AppendLine($"| Soft Delete Tables | {metadata.Statistics.SoftDeleteTables} |");
    markdown.AppendLine($"| Append-Only Tables | {metadata.Statistics.AppendOnlyTables} |");
    markdown.AppendLine($"| Polymorphic Tables | {metadata.Statistics.PolymorphicTables} |");
    markdown.AppendLine($"| Total Columns | {metadata.Statistics.TotalColumns} |");
    markdown.AppendLine($"| Total Constraints | {metadata.Statistics.TotalConstraints} |");
    markdown.AppendLine();
  }

  private static void GenerateErDiagrams(StringBuilder markdown, SchemaMetadata metadata)
  {
    markdown.AppendLine("## Entity Relationship Diagrams");
    markdown.AppendLine();

    foreach (IGrouping<string, TableMetadata>? category in metadata.Tables.GroupBy(t => t.Category ?? "Uncategorised").OrderBy(g => g.Key))
    {
      markdown.AppendLine($"### {category.Key} - ER Diagram");
      markdown.AppendLine();
      markdown.AppendLine("```mermaid");
      markdown.AppendLine("erDiagram");

      foreach (TableMetadata? table in category)
      {
        markdown.AppendLine($"    {table.Name} {{");

        int columnCount = 0;
        foreach (ColumnMetadata col in table.Columns)
        {
          if (columnCount >= 10)
            break; // Limit to first 10 columns for readability

          string pk = col.IsPrimaryKey ? "PK" : "";
          string fk = col.ForeignKey != null ? "FK" : "";
          string marker = !string.IsNullOrEmpty(pk) ? pk : fk;

          markdown.AppendLine($"        {col.Type} {col.Name} {marker}");
          columnCount++;
        }

        if (table.Columns.Count > 10)
        {
          markdown.AppendLine($"        ... {table.Columns.Count - 10} more columns");
        }

        markdown.AppendLine("    }");
      }

      foreach (TableMetadata? table in category)
      {
        foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
        {
          // Only show relationships within this category
          if (category.Any(t => t.Name == fk.ReferencedTable))
          {
            markdown.AppendLine($"    {table.Name} }}o--|| {fk.ReferencedTable} : \"{fk.Name}\"");
          }
        }
      }

      markdown.AppendLine("```");
      markdown.AppendLine();
    }
  }

  private void GenerateTableDocumentation(StringBuilder markdown, SchemaMetadata metadata)
  {
    foreach (IGrouping<string, TableMetadata>? category in metadata.Tables.GroupBy(t => t.Category ?? "Uncategorised").OrderBy(g => g.Key))
    {
      string categorySlug = category.Key.ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
      markdown.AppendLine($"<a id=\"{categorySlug}\"></a>");
      markdown.AppendLine();
      markdown.AppendLine($"## {category.Key}");
      markdown.AppendLine();

      if (metadata.Categories.TryGetValue(category.Key, out string? categoryDesc))
      {
        markdown.AppendLine($"*{categoryDesc}*");
        markdown.AppendLine();
      }

      foreach (TableMetadata? table in category.OrderBy(t => t.Name))
      {
        GenerateTableSection(markdown, table);
      }
    }
  }

  private void GenerateTableSection(StringBuilder markdown, TableMetadata table)
  {
    markdown.AppendLine($"### {table.Name}");
    markdown.AppendLine();

    if (!string.IsNullOrEmpty(table.Description))
    {
      markdown.AppendLine($"> {table.Description}");
      markdown.AppendLine();
    }

    var properties = new List<string>();
    if (table.HasTemporalVersioning)
      properties.Add("[Temporal]");
    if (table.HasSoftDelete)
      properties.Add("[Soft Delete]");
    if (table.IsAppendOnly)
      properties.Add("[Append-Only]");
    if (table.IsPolymorphic)
      properties.Add("[Polymorphic]");

    if (properties.Count > 0)
    {
      markdown.AppendLine($"**Properties:** {string.Join(" | ", properties)}");
      markdown.AppendLine();
    }

    if (table.HasTemporalVersioning && !string.IsNullOrEmpty(table.HistoryTable))
    {
      markdown.AppendLine($"**History Table:** `{table.HistoryTable}`");
      markdown.AppendLine();
    }

    markdown.AppendLine("#### Columns");
    markdown.AppendLine();
    markdown.AppendLine("| Column | Type | Nullable | Constraints | Description |");
    markdown.AppendLine("|--------|------|----------|-------------|-------------|");

    foreach (ColumnMetadata col in table.Columns)
    {
      var constraints = new List<string>();
      if (col.IsPrimaryKey)
        constraints.Add("PK");
      if (col.IsUnique)
        constraints.Add("UNIQUE");
      if (col.IsIdentity)
        constraints.Add("IDENTITY");
      if (col.IsComputed)
        constraints.Add("COMPUTED");
      if (col.ForeignKey != null)
        constraints.Add($"FK->{col.ForeignKey.Table}");
      if (!string.IsNullOrEmpty(col.DefaultValue))
        constraints.Add($"DEFAULT {col.DefaultValue}");

      string constraintStr = string.Join(", ", constraints);
      string description = col.Description ?? "";

      markdown.AppendLine(
          $"| `{col.Name}` | {col.Type} | {(col.Nullable ? "+" : "")} | {constraintStr} | {description} |");
    }

    markdown.AppendLine();


    if (GetDocSetting(IncludeConstraints, _config.Documentation.IncludeConstraints))
    {
      GenerateConstraintsSection(markdown, table);
    }

    if (GetDocSetting(IncludeIndexes, _config.Documentation.IncludeIndexes) && table.Indexes.Count > 0)
    {
      GenerateIndexesSection(markdown, table);
    }

    markdown.AppendLine();
  }

  private static void GenerateConstraintsSection(StringBuilder markdown, TableMetadata table)
  {
    bool hasConstraints = table.Constraints.ForeignKeys.Count > 0 ||
                         table.Constraints.UniqueConstraints.Count > 0 ||
                         table.Constraints.CheckConstraints.Count > 0;

    if (!hasConstraints)
      return;

    markdown.AppendLine("#### Constraints");
    markdown.AppendLine();

    if (table.Constraints.ForeignKeys.Count > 0)
    {
      markdown.AppendLine("**Foreign Keys:**");
      markdown.AppendLine();
      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        string columns = string.Join(", ", fk.Columns);
        string refColumns = string.Join(", ", fk.ReferencedColumns);
        string actions = FormatFkActions(fk.OnDelete, fk.OnUpdate);
        markdown.AppendLine($"- `{fk.Name}`: ({columns}) -> {fk.ReferencedTable}({refColumns}){actions}");
      }
      markdown.AppendLine();
    }

    if (table.Constraints.UniqueConstraints.Count > 0)
    {
      markdown.AppendLine("**Unique Constraints:**");
      markdown.AppendLine();
      foreach (UniqueConstraint uq in table.Constraints.UniqueConstraints)
      {
        string columns = string.Join(", ", uq.Columns);
        string filter = !string.IsNullOrEmpty(uq.FilterClause) ? $" {uq.FilterClause}" : "";
        markdown.AppendLine($"- `{uq.Name}`: ({columns}){filter}");
      }
      markdown.AppendLine();
    }

    if (table.Constraints.CheckConstraints.Count > 0)
    {
      markdown.AppendLine("**Check Constraints:**");
      markdown.AppendLine();
      foreach (CheckConstraint ck in table.Constraints.CheckConstraints)
      {
        markdown.AppendLine($"- `{ck.Name}`: `{ck.Expression}`");
      }
      markdown.AppendLine();
    }
  }

  private static void GenerateIndexesSection(StringBuilder markdown, TableMetadata table)
  {
    markdown.AppendLine("#### Indexes");
    markdown.AppendLine();

    foreach (IndexMetadata idx in table.Indexes)
    {
      string type = idx.IsUnique ? "UNIQUE" : "";
      string clustered = idx.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
      string columns = string.Join(", ", idx.Columns.Select(c => c.Name + (c.IsDescending ? " DESC" : "")));
      string filter = !string.IsNullOrEmpty(idx.FilterClause) ? $" {idx.FilterClause}" : "";

      markdown.AppendLine($"- `{idx.Name}`: {type} {clustered} ({columns}){filter}");

      if (idx.IncludedColumns != null && idx.IncludedColumns.Count > 0)
      {
        markdown.AppendLine($"  - INCLUDE: ({string.Join(", ", idx.IncludedColumns)})");
      }
    }

    markdown.AppendLine();
  }

  /// <summary>
  /// Formats FK referential actions for documentation output.
  /// Only includes non-default actions (NO ACTION is the SQL Server default).
  /// </summary>
  private static string FormatFkActions(ForeignKeyAction onDelete, ForeignKeyAction onUpdate)
  {
    var actions = new List<string>();

    if (onDelete != ForeignKeyAction.NoAction)
    {
      actions.Add($"ON DELETE {FormatAction(onDelete)}");
    }

    if (onUpdate != ForeignKeyAction.NoAction)
    {
      actions.Add($"ON UPDATE {FormatAction(onUpdate)}");
    }

    return actions.Count > 0 ? $" [{string.Join(", ", actions)}]" : "";
  }

  /// <summary>
  /// Converts a <see cref="ForeignKeyAction"/> to SQL syntax.
  /// </summary>
  private static string FormatAction(ForeignKeyAction action)
  {
    return action switch
    {
      ForeignKeyAction.Cascade => "CASCADE",
      ForeignKeyAction.SetNull => "SET NULL",
      ForeignKeyAction.SetDefault => "SET DEFAULT",
      _ => "NO ACTION"
    };
  }
}
