using System.Text;
using SchemaTools.Configuration;
using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Generates comprehensive Markdown documentation from schema metadata.
/// Supports history table filtering, infrastructure column grouping,
/// cross-category ER diagram relationships, and FK cross-references.
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

  /// <summary>
  /// Immutable bundle of pre-computed rendering state, passed through
  /// the generation pipeline to avoid repeated computation and mutable fields.
  /// </summary>
  private sealed record RenderContext(
      SchemaMetadata Metadata,
      SchemaToolsConfig Config,
      IReadOnlyList<TableMetadata> DocumentedTables,
      IReadOnlyList<TableMetadata> AuthoredTables,
      IReadOnlyList<TableMetadata> HistoryTables,
      HashSet<string> DocumentedTableNames,
      bool IncludeStatistics,
      bool IncludeErDiagrams,
      bool IncludeConstraints,
      bool IncludeIndexes);

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
        return false;

      Log.LogMessage($"Generating documentation for {metadata.Tables.Count} tables...");

      var authoredTables = metadata.Tables.Where(t => !t.IsHistoryTable).ToList();
      var historyTables = metadata.Tables.Where(t => t.IsHistoryTable).ToList();
      HistoryTableMode historyMode = _config.Documentation.HistoryTables;

      IReadOnlyList<TableMetadata> documentedTables = historyMode == HistoryTableMode.Full
          ? metadata.Tables.ToList()
          : authoredTables;

      var ctx = new RenderContext(
          Metadata: metadata,
          Config: _config,
          DocumentedTables: documentedTables,
          AuthoredTables: authoredTables,
          HistoryTables: historyTables,
          DocumentedTableNames: new HashSet<string>(
              documentedTables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase),
          IncludeStatistics: GetDocSetting(IncludeStatistics, _config.Documentation.IncludeStatistics),
          IncludeErDiagrams: GetDocSetting(IncludeErDiagrams, _config.Documentation.IncludeErDiagrams),
          IncludeConstraints: GetDocSetting(IncludeConstraints, _config.Documentation.IncludeConstraints),
          IncludeIndexes: GetDocSetting(IncludeIndexes, _config.Documentation.IncludeIndexes));

      string markdown = GenerateDocument(ctx);

      GenerationUtilities.EnsureDirectoryExists(OutputFile);
      File.WriteAllText(OutputFile, markdown, Encoding.UTF8);

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          $"+ Documentation written to: {OutputFile}");
      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Failed to generate documentation: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  // ---------------------------------------------------------------------------
  // Document assembly
  // ---------------------------------------------------------------------------

  private static string GenerateDocument(RenderContext ctx)
  {
    var md = new StringBuilder();

    GenerateHeader(md, ctx.Metadata);
    GenerateTableOfContents(md, ctx);

    if (ctx.IncludeStatistics)
      GenerateStatistics(md, ctx.AuthoredTables, ctx.HistoryTables);

    if (ctx.IncludeErDiagrams)
      GenerateErDiagrams(md, ctx);

    GenerateTableDocumentation(md, ctx);

    if (ctx.Config.Documentation.HistoryTables == HistoryTableMode.Compact && ctx.HistoryTables.Count > 0)
      GenerateCompactHistorySection(md, ctx);

    return md.ToString();
  }

  // ---------------------------------------------------------------------------
  // Column classification
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Returns true when the column is a system-managed infrastructure column
  /// (soft-delete flag, audit trail, or temporal period) rather than a
  /// domain column defined by the application schema.
  /// </summary>
  private static bool IsInfrastructureColumn(ColumnMetadata column, ColumnNamingConfig columnConfig)
  {
    if (column.IsGeneratedAlways)
      return true;

    string name = column.Name;
    return string.Equals(name, columnConfig.Active, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, columnConfig.CreatedAt, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, columnConfig.CreatedBy, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, columnConfig.UpdatedBy, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, columnConfig.ValidFrom, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, columnConfig.ValidTo, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Returns a concise auto-generated description for infrastructure columns.
  /// Returns null for domain columns or when no description applies.
  /// </summary>
  private static string? GetInfrastructureDescription(ColumnMetadata column, ColumnNamingConfig columnConfig)
  {
    string name = column.Name;

    if (string.Equals(name, columnConfig.Active, StringComparison.OrdinalIgnoreCase))
      return "Soft-delete flag";
    if (string.Equals(name, columnConfig.CreatedAt, StringComparison.OrdinalIgnoreCase))
      return "Creation timestamp";
    if (string.Equals(name, columnConfig.CreatedBy, StringComparison.OrdinalIgnoreCase))
      return "Creator reference";
    if (string.Equals(name, columnConfig.UpdatedBy, StringComparison.OrdinalIgnoreCase))
      return "Last modifier reference";
    if (string.Equals(name, columnConfig.ValidFrom, StringComparison.OrdinalIgnoreCase))
      return "Period start";
    if (string.Equals(name, columnConfig.ValidTo, StringComparison.OrdinalIgnoreCase))
      return "Period end";

    if (column.IsGeneratedAlways)
    {
      return column.GeneratedAlwaysType switch
      {
        GeneratedAlwaysType.RowStart => "Period start",
        GeneratedAlwaysType.RowEnd => "Period end",
        _ => null
      };
    }

    return null;
  }

  // ---------------------------------------------------------------------------
  // Document header
  // ---------------------------------------------------------------------------

  private static void GenerateHeader(StringBuilder md, SchemaMetadata metadata)
  {
    md.AppendLine("# Database Schema Documentation");
    md.AppendLine();
    md.AppendLine($"**Database:** {metadata.Database}  ");
    md.AppendLine($"**Generated:** {metadata.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC  ");
    md.AppendLine($"**Tool Version:** {metadata.ToolVersion}  ");
    md.AppendLine($"**SQL Server:** {metadata.SqlServerVersion}");
    md.AppendLine();
  }

  // ---------------------------------------------------------------------------
  // Table of contents
  // ---------------------------------------------------------------------------

  private static void GenerateTableOfContents(StringBuilder md, RenderContext ctx)
  {
    md.AppendLine("## Table of Contents");
    md.AppendLine();

    // Global section links on a single line
    var sectionLinks = new List<string>();
    if (ctx.IncludeStatistics)
      sectionLinks.Add("[Statistics](#statistics)");
    if (ctx.IncludeErDiagrams)
      sectionLinks.Add("[ER Diagrams](#entity-relationship-diagrams)");
    if (ctx.Config.Documentation.HistoryTables == HistoryTableMode.Compact && ctx.HistoryTables.Count > 0)
      sectionLinks.Add("[History Tables](#history-tables)");

    if (sectionLinks.Count > 0)
    {
      md.AppendLine(string.Join(" -- ", sectionLinks));
      md.AppendLine();
    }

    // Category table: one row per category with inline table links
    md.AppendLine("| Category | Tables |");
    md.AppendLine("|----------|--------|");

    foreach (IGrouping<string, TableMetadata> category in ctx.DocumentedTables
        .GroupBy(t => t.Category ?? "Uncategorised")
        .OrderBy(g => g.Key))
    {
      string categorySlug = ToSlug(category.Key);
      string categoryLink = $"[{category.Key}](#{categorySlug})";
      string tableLinks = string.Join(", ", category
          .OrderBy(t => t.Name)
          .Select(t => $"[{t.Name}](#{t.Name.ToLowerInvariant()})"));
      md.AppendLine($"| {categoryLink} | {tableLinks} |");
    }

    md.AppendLine();
  }

  // ---------------------------------------------------------------------------
  // Statistics
  // ---------------------------------------------------------------------------

  private static void GenerateStatistics(StringBuilder md,
      IReadOnlyList<TableMetadata> authoredTables,
      IReadOnlyList<TableMetadata> historyTables)
  {
    int tableCount = authoredTables.Count;
    int temporalCount = authoredTables.Count(t => t.HasTemporalVersioning);
    int historyCount = historyTables.Count;
    int softDeleteCount = authoredTables.Count(t => t.HasSoftDelete);
    int appendOnlyCount = authoredTables.Count(t => t.IsAppendOnly);
    int polymorphicCount = authoredTables.Count(t => t.IsPolymorphic);
    int columnCount = authoredTables.Sum(t => t.Columns.Count);
    int constraintCount = authoredTables.Sum(t => CountConstraints(t.Constraints));

    md.AppendLine("## Statistics");
    md.AppendLine();
    md.AppendLine("| Metric | Count |");
    md.AppendLine("|--------|-------|");
    md.AppendLine($"| Tables | {tableCount} |");
    if (temporalCount > 0)
      md.AppendLine($"| Temporal | {temporalCount} |");
    if (historyCount > 0)
      md.AppendLine($"| History Tables | {historyCount} |");
    if (softDeleteCount > 0)
      md.AppendLine($"| Soft Delete | {softDeleteCount} |");
    if (appendOnlyCount > 0)
      md.AppendLine($"| Append-Only | {appendOnlyCount} |");
    if (polymorphicCount > 0)
      md.AppendLine($"| Polymorphic | {polymorphicCount} |");
    md.AppendLine($"| Columns | {columnCount} |");
    md.AppendLine($"| Constraints | {constraintCount} |");
    md.AppendLine();
  }

  // ---------------------------------------------------------------------------
  // ER diagrams (Mermaid)
  // ---------------------------------------------------------------------------

  private static void GenerateErDiagrams(StringBuilder md, RenderContext ctx)
  {
    IReadOnlyList<TableMetadata> authoredTables = ctx.AuthoredTables;
    bool domainOnly = ctx.Config.Documentation.ErDiagramDomainColumnsOnly;
    ColumnNamingConfig columnConfig = ctx.Config.Columns;

    md.AppendLine("## Entity Relationship Diagrams");
    md.AppendLine();

    foreach (IGrouping<string, TableMetadata> category in authoredTables
        .GroupBy(t => t.Category ?? "Uncategorised")
        .OrderBy(g => g.Key))
    {
      md.AppendLine($"### {category.Key} - ER Diagram");
      md.AppendLine();
      md.AppendLine("```mermaid");
      md.AppendLine("erDiagram");

      var categoryTableNames = new HashSet<string>(
          category.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

      // Collect cross-category FK targets (stub entities)
      var externalStubs = new Dictionary<string, TableMetadata>(StringComparer.OrdinalIgnoreCase);
      foreach (TableMetadata table in category)
      {
        foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
        {
          if (!categoryTableNames.Contains(fk.ReferencedTable)
              && !externalStubs.ContainsKey(fk.ReferencedTable))
          {
            TableMetadata? refTable = authoredTables.FirstOrDefault(t =>
                string.Equals(t.Name, fk.ReferencedTable, StringComparison.OrdinalIgnoreCase));
            if (refTable != null)
              externalStubs[refTable.Name] = refTable;
          }
        }
      }

      // Full entities for tables in this category
      foreach (TableMetadata table in category)
      {
        RenderErEntity(md, table, domainOnly, columnConfig);
      }

      // Stub entities for cross-category FK targets (PK columns only)
      foreach (TableMetadata stub in externalStubs.Values.OrderBy(t => t.Name))
      {
        RenderErStubEntity(md, stub);
      }

      // Relationship lines (within-category and cross-category)
      foreach (TableMetadata table in category)
      {
        foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
        {
          if (categoryTableNames.Contains(fk.ReferencedTable)
              || externalStubs.ContainsKey(fk.ReferencedTable))
          {
            md.AppendLine($"    {table.Name} }}o--|| {fk.ReferencedTable} : \"{fk.Name}\"");
          }
        }
      }

      md.AppendLine("```");
      md.AppendLine();
    }
  }

  private static void RenderErEntity(StringBuilder md, TableMetadata table,
      bool domainOnly, ColumnNamingConfig columnConfig)
  {
    md.AppendLine($"    {table.Name} {{");

    foreach (ColumnMetadata col in table.Columns)
    {
      if (domainOnly && IsInfrastructureColumn(col, columnConfig))
        continue;

      string pk = col.IsPrimaryKey ? "PK" : "";
      string fk = col.ForeignKey != null ? "FK" : "";
      string marker = !string.IsNullOrEmpty(pk) ? pk : fk;

      md.AppendLine($"        {col.Type} {col.Name} {marker}");
    }

    md.AppendLine("    }");
  }

  /// <summary>
  /// Renders a minimal stub entity showing only primary key columns,
  /// used to represent cross-category FK targets.
  /// </summary>
  private static void RenderErStubEntity(StringBuilder md, TableMetadata table)
  {
    md.AppendLine($"    {table.Name} {{");

    foreach (ColumnMetadata col in table.Columns.Where(c => c.IsPrimaryKey))
    {
      md.AppendLine($"        {col.Type} {col.Name} PK");
    }

    md.AppendLine("    }");
  }

  // ---------------------------------------------------------------------------
  // Table documentation
  // ---------------------------------------------------------------------------

  private static void GenerateTableDocumentation(StringBuilder md, RenderContext ctx)
  {
    foreach (IGrouping<string, TableMetadata> category in ctx.DocumentedTables
        .GroupBy(t => t.Category ?? "Uncategorised")
        .OrderBy(g => g.Key))
    {
      string categorySlug = ToSlug(category.Key);
      md.AppendLine($"<a id=\"{categorySlug}\"></a>");
      md.AppendLine();
      md.AppendLine($"## {category.Key}");
      md.AppendLine();

      if (ctx.Metadata.Categories.TryGetValue(category.Key, out string? categoryDesc))
      {
        md.AppendLine($"*{categoryDesc}*");
        md.AppendLine();
      }

      foreach (TableMetadata table in category.OrderBy(t => t.Name))
      {
        GenerateTableSection(md, table, ctx);
      }
    }
  }

  private static void GenerateTableSection(StringBuilder md, TableMetadata table, RenderContext ctx)
  {
    md.AppendLine($"### {table.Name}");
    md.AppendLine();

    if (!string.IsNullOrEmpty(table.Description))
    {
      md.AppendLine($"> {table.Description}");
      md.AppendLine();
    }

    var properties = new List<string>();
    if (table.HasTemporalVersioning)
      properties.Add("`Temporal`");
    if (table.HasSoftDelete)
      properties.Add("`Soft Delete`");
    if (table.IsAppendOnly)
      properties.Add("`Append-Only`");
    if (table.IsPolymorphic)
      properties.Add("`Polymorphic`");

    if (properties.Count > 0)
    {
      md.AppendLine($"**Properties:** {string.Join(" | ", properties)}");
      md.AppendLine();
    }

    if (table.HasTemporalVersioning && !string.IsNullOrEmpty(table.HistoryTable))
    {
      string histRef = FormatHistoryTableReference(
          table.HistoryTable!, ctx.Config.Documentation.HistoryTables);
      md.AppendLine($"**History Table:** {histRef}");
      md.AppendLine();
    }

    GenerateColumnTable(md, table, ctx);

    if (ctx.IncludeConstraints)
      GenerateConstraintsSection(md, table, ctx.DocumentedTableNames);

    if (ctx.IncludeIndexes && table.Indexes.Count > 0)
      GenerateIndexesSection(md, table);

    md.AppendLine();
  }

  // ---------------------------------------------------------------------------
  // Column table
  // ---------------------------------------------------------------------------

  private static void GenerateColumnTable(StringBuilder md, TableMetadata table, RenderContext ctx)
  {
    ColumnNamingConfig columnConfig = ctx.Config.Columns;
    bool styling = ctx.Config.Documentation.InfrastructureColumnStyling;

    md.AppendLine("| Column | Type | Nullable | Constraints | Description |");
    md.AppendLine("|--------|------|----------|-------------|-------------|");

    if (styling)
    {
      var domainCols = table.Columns
          .Where(c => !IsInfrastructureColumn(c, columnConfig)).ToList();
      var infraCols = table.Columns
          .Where(c => IsInfrastructureColumn(c, columnConfig)).ToList();

      foreach (ColumnMetadata col in domainCols)
        RenderColumnRow(md, col, ctx.DocumentedTableNames, columnConfig, isInfrastructure: false);

      if (domainCols.Count > 0 && infraCols.Count > 0)
        md.AppendLine("|  |  |  |  |  |");

      foreach (ColumnMetadata col in infraCols)
        RenderColumnRow(md, col, ctx.DocumentedTableNames, columnConfig, isInfrastructure: true);
    }
    else
    {
      foreach (ColumnMetadata col in table.Columns)
        RenderColumnRow(md, col, ctx.DocumentedTableNames, columnConfig, isInfrastructure: false);
    }

    md.AppendLine();
  }

  private static void RenderColumnRow(StringBuilder md, ColumnMetadata col,
      HashSet<string> documentedTableNames, ColumnNamingConfig columnConfig,
      bool isInfrastructure)
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
    if (col.IsGeneratedAlways)
      constraints.Add("GENERATED ALWAYS");
    if (col.ForeignKey != null)
    {
      string target = col.ForeignKey.Table;
      string fkRef = documentedTableNames.Contains(target)
          ? $"FK -> [{target}](#{target.ToLowerInvariant()})"
          : $"FK -> {target}";
      constraints.Add(fkRef);
    }
    if (!string.IsNullOrEmpty(col.DefaultValue))
      constraints.Add($"DEFAULT {col.DefaultValue}");

    string constraintStr = string.Join(", ", constraints);

    // Explicit description takes precedence; auto-description is a fallback
    // for infrastructure columns only.
    string description = col.Description ?? "";
    if (isInfrastructure && string.IsNullOrEmpty(description))
      description = GetInfrastructureDescription(col, columnConfig) ?? "";

    // Computed column: show expression when no explicit description exists
    if (col.IsComputed && string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(col.ComputedExpression))
      description = $"`{col.ComputedExpression}`";

    string colName = isInfrastructure ? $"*`{col.Name}`*" : $"`{col.Name}`";
    md.AppendLine(
        $"| {colName} | {col.Type} | {(col.Nullable ? "+" : "")} | {constraintStr} | {description} |");
  }

  // ---------------------------------------------------------------------------
  // Constraints section
  // ---------------------------------------------------------------------------

  private static void GenerateConstraintsSection(StringBuilder md, TableMetadata table,
      HashSet<string> documentedTableNames)
  {
    bool hasConstraints = table.Constraints.ForeignKeys.Count > 0
        || table.Constraints.UniqueConstraints.Count > 0
        || table.Constraints.CheckConstraints.Count > 0;

    if (!hasConstraints)
      return;

    md.AppendLine("#### Constraints");
    md.AppendLine();

    if (table.Constraints.ForeignKeys.Count > 0)
    {
      md.AppendLine("**Foreign Keys:**");
      md.AppendLine();
      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        string columns = string.Join(", ", fk.Columns);
        string refColumns = string.Join(", ", fk.ReferencedColumns);
        string actions = FormatFkActions(fk.OnDelete, fk.OnUpdate);

        string tableRef = documentedTableNames.Contains(fk.ReferencedTable)
            ? $"[{fk.ReferencedTable}](#{fk.ReferencedTable.ToLowerInvariant()})"
            : fk.ReferencedTable;

        md.AppendLine($"- `{fk.Name}`: ({columns}) -> {tableRef}({refColumns}){actions}");
      }
      md.AppendLine();
    }

    if (table.Constraints.UniqueConstraints.Count > 0)
    {
      md.AppendLine("**Unique Constraints:**");
      md.AppendLine();
      foreach (UniqueConstraint uq in table.Constraints.UniqueConstraints)
      {
        string columns = string.Join(", ", uq.Columns);
        string filter = !string.IsNullOrEmpty(uq.FilterClause) ? $" {uq.FilterClause}" : "";
        md.AppendLine($"- `{uq.Name}`: ({columns}){filter}");
      }
      md.AppendLine();
    }

    if (table.Constraints.CheckConstraints.Count > 0)
    {
      md.AppendLine("**Check Constraints:**");
      md.AppendLine();
      foreach (CheckConstraint ck in table.Constraints.CheckConstraints)
      {
        md.AppendLine($"- `{ck.Name}`: `{ck.Expression}`");
      }
      md.AppendLine();
    }
  }

  // ---------------------------------------------------------------------------
  // Indexes section
  // ---------------------------------------------------------------------------

  private static void GenerateIndexesSection(StringBuilder md, TableMetadata table)
  {
    md.AppendLine("#### Indexes");
    md.AppendLine();

    foreach (IndexMetadata idx in table.Indexes)
    {
      string clustered = idx.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
      string prefix = idx.IsUnique ? $"UNIQUE {clustered}" : clustered;
      string columns = string.Join(", ", idx.Columns.Select(c => c.Name + (c.IsDescending ? " DESC" : "")));
      string filter = !string.IsNullOrEmpty(idx.FilterClause) ? $" {idx.FilterClause}" : "";

      md.AppendLine($"- `{idx.Name}`: {prefix} ({columns}){filter}");

      if (idx.IncludedColumns != null && idx.IncludedColumns.Count > 0)
      {
        md.AppendLine($"  - INCLUDE: ({string.Join(", ", idx.IncludedColumns)})");
      }
    }

    md.AppendLine();
  }

  // ---------------------------------------------------------------------------
  // Compact history section
  // ---------------------------------------------------------------------------

  private static void GenerateCompactHistorySection(StringBuilder md, RenderContext ctx)
  {
    md.AppendLine("<a id=\"history-tables\"></a>");
    md.AppendLine();
    md.AppendLine("## History Tables");
    md.AppendLine();
    md.AppendLine("The following temporal history tables are managed by SQL Server:");
    md.AppendLine();
    md.AppendLine("| History Table | Source Table | Columns |");
    md.AppendLine("|---------------|-------------|---------|");

    foreach (TableMetadata history in ctx.HistoryTables.OrderBy(t => t.Name))
    {
      // Locate the source table that owns this history table
      TableMetadata? source = ctx.AuthoredTables.FirstOrDefault(t =>
          !string.IsNullOrEmpty(t.HistoryTable)
          && string.Equals(ExtractTableName(t.HistoryTable!), history.Name,
              StringComparison.OrdinalIgnoreCase));

      string sourceRef = source != null
          ? $"[{source.Name}](#{source.Name.ToLowerInvariant()})"
          : "-";

      md.AppendLine($"| {history.Name} | {sourceRef} | {history.Columns.Count} |");
    }

    md.AppendLine();
  }

  // ---------------------------------------------------------------------------
  // Formatting helpers
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Formats a history table reference as a plain name, anchor link,
  /// or section link depending on the configured history table mode.
  /// </summary>
  private static string FormatHistoryTableReference(string historyTable, HistoryTableMode mode)
  {
    string name = ExtractTableName(historyTable);
    return mode switch
    {
      HistoryTableMode.Compact => $"[{name}](#history-tables)",
      HistoryTableMode.Full => $"[{name}](#{name.ToLowerInvariant()})",
      _ => name
    };
  }

  /// <summary>
  /// Formats FK referential actions for documentation output.
  /// Only includes non-default actions (NO ACTION is the SQL Server default).
  /// </summary>
  private static string FormatFkActions(ForeignKeyAction onDelete, ForeignKeyAction onUpdate)
  {
    var actions = new List<string>();

    if (onDelete != ForeignKeyAction.NoAction)
      actions.Add($"ON DELETE {FormatAction(onDelete)}");

    if (onUpdate != ForeignKeyAction.NoAction)
      actions.Add($"ON UPDATE {FormatAction(onUpdate)}");

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

  private static int CountConstraints(ConstraintsCollection constraints)
  {
    return (constraints.PrimaryKey != null ? 1 : 0)
        + constraints.ForeignKeys.Count
        + constraints.UniqueConstraints.Count
        + constraints.CheckConstraints.Count;
  }

  /// <summary>
  /// Extracts the table name from a schema-qualified reference
  /// (e.g. "[dbo].[users_history]" returns "users_history").
  /// </summary>
  private static string ExtractTableName(string qualifiedName)
  {
    int lastDot = qualifiedName.LastIndexOf('.');
    string name = lastDot >= 0 ? qualifiedName[(lastDot + 1)..] : qualifiedName;
    return name.Trim('[', ']');
  }

  /// <summary>
  /// Converts heading text to a GFM-compatible anchor slug.
  /// </summary>
  private static string ToSlug(string text)
  {
    return text.ToLowerInvariant().Replace(" ", "-");
  }

  // ---------------------------------------------------------------------------
  // Configuration and metadata loading
  // ---------------------------------------------------------------------------

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
}
