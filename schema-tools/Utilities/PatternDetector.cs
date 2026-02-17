using SchemaTools.Configuration;
using SchemaTools.Models;

namespace SchemaTools.Utilities;

/// <summary>
/// Detects table patterns (soft-delete, append-only, polymorphic) from extracted
/// metadata. Operates purely on SchemaTools model types with no DacFx dependency,
/// making it available under both netstandard2.0 and net10.0 targets.
/// </summary>
internal static class PatternDetector
{
  /// <summary>
  /// Detects soft-delete, append-only, and polymorphic patterns for a single table
  /// based on its columns and the effective configuration.
  /// Returns a new <see cref="TableMetadata"/> with pattern flags applied.
  /// </summary>
  internal static TableMetadata DetectTablePatterns(TableMetadata table, SchemaToolsConfig config, SqlServerVersion sqlVersion)
  {
    SchemaToolsConfig effective = config.ResolveForTable(table.Name, table.Category);

    string? activeColumnName = table.ActiveColumnName;
    bool hasSoftDelete = table.HasSoftDelete;
    bool isAppendOnly = table.IsAppendOnly;
    bool isPolymorphic = table.IsPolymorphic;
    PolymorphicOwnerInfo? polymorphicOwner = table.PolymorphicOwner;
    IReadOnlyList<ColumnMetadata> columns = table.Columns;

    if (table.HasActiveColumn)
    {
      activeColumnName = effective.Columns.Active;
    }

    if (effective.Features.EnableSoftDelete &&
        table.HasActiveColumn &&
        table.HasTemporalVersioning)
    {
      hasSoftDelete = true;
    }

    if (effective.Features.DetectAppendOnlyTables)
    {
      bool hasCreatedAt = table.Columns.Any(c =>
        string.Equals(c.Name, effective.Columns.CreatedAt, StringComparison.OrdinalIgnoreCase));
      bool hasUpdatedBy = table.Columns.Any(c =>
        string.Equals(c.Name, effective.Columns.UpdatedBy, StringComparison.OrdinalIgnoreCase));

      if (hasCreatedAt && !hasUpdatedBy && !table.HasTemporalVersioning)
      {
        isAppendOnly = true;
      }
    }

    // Polymorphic detection (skip history tables -- they inherit columns but lack CHECK constraints)
    if (effective.Features.DetectPolymorphicPatterns && !table.IsHistoryTable)
    {
      foreach (PolymorphicPatternConfig pattern in effective.Columns.PolymorphicPatterns)
      {
        bool hasTypeCol = table.Columns.Any(c =>
          string.Equals(c.Name, pattern.TypeColumn, StringComparison.OrdinalIgnoreCase));
        bool hasIdCol = table.Columns.Any(c =>
          string.Equals(c.Name, pattern.IdColumn, StringComparison.OrdinalIgnoreCase));

        if (hasTypeCol && hasIdCol)
        {
          isPolymorphic = true;
          polymorphicOwner = new PolymorphicOwnerInfo
          {
            TypeColumn = pattern.TypeColumn,
            IdColumn = pattern.IdColumn,
            AllowedTypes = ExtractAllowedTypesForPolymorphic(table, pattern.TypeColumn, sqlVersion)
          };

          List<ColumnMetadata> modifiedColumns = columns.ToList();
          for (int i = 0; i < modifiedColumns.Count; i++)
          {
            if (string.Equals(modifiedColumns[i].Name, pattern.TypeColumn, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(modifiedColumns[i].Name, pattern.IdColumn, StringComparison.OrdinalIgnoreCase))
            {
              modifiedColumns[i] = modifiedColumns[i] with { IsPolymorphicForeignKey = true };
            }
          }
          columns = modifiedColumns;

          break;
        }
      }
    }

    return table with
    {
      ActiveColumnName = activeColumnName,
      HasSoftDelete = hasSoftDelete,
      IsAppendOnly = isAppendOnly,
      IsPolymorphic = isPolymorphic,
      PolymorphicOwner = polymorphicOwner,
      Columns = columns
    };
  }

  /// <summary>
  /// Marks tables that are temporal history tables. These tables are referenced by
  /// another table's <see cref="TableMetadata.HistoryTable"/> and do not have
  /// primary keys by design.
  /// Returns a new list with history-table flags applied.
  /// </summary>
  internal static IReadOnlyList<TableMetadata> MarkHistoryTables(IReadOnlyList<TableMetadata> tables)
  {
    var historyTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (TableMetadata table in tables)
    {
      if (!string.IsNullOrEmpty(table.HistoryTable))
      {
        // HistoryTable format is "[schema].[name]" -- extract just the name
        string historyName = table.HistoryTable!
          .Replace("[", "").Replace("]", "")
          .Split('.').LastOrDefault() ?? "";
        if (!string.IsNullOrEmpty(historyName))
        {
          historyTableNames.Add(historyName);
        }
      }
    }

    List<TableMetadata> result = tables.ToList();
    for (int i = 0; i < result.Count; i++)
    {
      if (historyTableNames.Contains(result[i].Name))
      {
        result[i] = result[i] with { IsHistoryTable = true };
      }
    }

    return result;
  }

  /// <summary>
  /// Extracts allowed polymorphic types from CHECK constraints that reference the
  /// given type column. Uses ScriptDom AST parsing for reliable string literal extraction.
  /// </summary>
  private static List<string> ExtractAllowedTypesForPolymorphic(
    TableMetadata table, string typeColumn, SqlServerVersion sqlVersion)
  {
    var types = new List<string>();

    foreach (CheckConstraint cc in table.Constraints.CheckConstraints)
    {
      if (string.IsNullOrEmpty(cc.Expression))
      {
        continue;
      }

      // Check if this constraint references the type column (with or without brackets)
      if (cc.Expression.IndexOf(typeColumn, StringComparison.OrdinalIgnoreCase) < 0
          && cc.Expression.IndexOf($"[{typeColumn}]", StringComparison.OrdinalIgnoreCase) < 0)
      {
        continue;
      }

      // Use ScriptDom to reliably extract string literals from the expression
      List<string> extracted = ScriptDomParser.ExtractAllowedTypesFromExpression(cc.Expression, sqlVersion);
      types.AddRange(extracted);
    }

    return types;
  }
}
