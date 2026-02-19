using System.Text.RegularExpressions;
using SchemaTools.Configuration;
using SchemaTools.Models;

namespace SchemaTools.Tasks;

/// <summary>
/// Pure validation logic extracted from <see cref="SchemaValidator"/>.
/// All methods are deterministic, perform no I/O, and produce no side effects.
/// The <see cref="SchemaValidator"/> MSBuild task acts as the impure shell,
/// handling file loading, configuration resolution, and diagnostic reporting.
/// </summary>
internal static class SchemaValidation
{
  /// <summary>
  /// Aggregated validation result containing all errors and warnings.
  /// </summary>
  internal sealed record ValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

  private static readonly Regex SnakeCasePattern =
    new(@"^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

  /// <summary>
  /// Runs all configured validations against the schema metadata.
  /// Task-level overrides (nullable bools) take precedence over config values.
  /// Per-table config overrides are resolved within individual validation methods.
  /// </summary>
  internal static ValidationResult Validate(
    SchemaMetadata metadata,
    SchemaToolsConfig config,
    bool? validateForeignKeys = null,
    bool? validatePolymorphic = null,
    bool? validateTemporal = null,
    bool? validateAuditColumns = null,
    bool? enforceNamingConventions = null)
  {
    var errors = new List<string>();
    var warnings = new List<string>();

    if (validateForeignKeys ?? config.Validation.ValidateForeignKeys)
    {
      ValidateForeignKeyReferences(metadata, errors);
    }

    if (validatePolymorphic ?? config.Validation.ValidatePolymorphic)
    {
      ValidatePolymorphicTables(metadata, errors, warnings);
    }

    if (validateTemporal ?? config.Validation.ValidateTemporal)
    {
      ValidateTemporalTables(metadata, config, validateTemporal, errors, warnings);
    }

    if (validateAuditColumns ?? config.Validation.ValidateAuditColumns)
    {
      ValidateAuditColumnConsistency(metadata, config, validateAuditColumns, errors, warnings);
    }

    if (enforceNamingConventions ?? config.Validation.EnforceNamingConventions)
    {
      ValidateNamingConventions(metadata, warnings);
    }

    // Always-on validations (no config toggle)
    ValidatePrimaryKeys(metadata, errors);
    ValidateCircularForeignKeys(metadata, errors);
    ValidateSoftDeleteConsistency(metadata, errors);
    ValidateUniqueConstraints(metadata, config, errors, warnings);

    return new ValidationResult(errors, warnings);
  }

  /// <summary>
  /// Validates that all foreign key references point to existing tables and columns.
  /// </summary>
  internal static void ValidateForeignKeyReferences(SchemaMetadata metadata, List<string> errors)
  {
    var tableNames = new HashSet<string>(metadata.Tables.Select(t => t.Name));

    foreach (TableMetadata table in metadata.Tables)
    {
      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        if (!tableNames.Contains(fk.ReferencedTable))
        {
          errors.Add(
            $"{table.Name}: FK '{fk.Name}' references non-existent table '{fk.ReferencedTable}'");
        }

        TableMetadata? refTable = metadata.Tables.FirstOrDefault(t => t.Name == fk.ReferencedTable);
        if (refTable != null)
        {
          foreach (string refCol in fk.ReferencedColumns)
          {
            if (!refTable.Columns.Any(c => c.Name == refCol))
            {
              errors.Add(
                $"{table.Name}: FK '{fk.Name}' references non-existent column '{refCol}' in table '{fk.ReferencedTable}'");
            }
          }
        }

        if (fk.Columns.Count != fk.ReferencedColumns.Count)
        {
          errors.Add(
            $"{table.Name}: FK '{fk.Name}' has mismatched column counts ({fk.Columns.Count} vs {fk.ReferencedColumns.Count})");
        }
      }
    }
  }

  /// <summary>
  /// Validates polymorphic tables have required columns and CHECK constraints.
  /// </summary>
  internal static void ValidatePolymorphicTables(
    SchemaMetadata metadata, List<string> errors, List<string> warnings)
  {
    foreach (TableMetadata table in metadata.Tables.Where(t => t.IsPolymorphic && !t.IsHistoryTable))
    {
      if (table.PolymorphicOwner == null)
      {
        errors.Add($"{table.Name}: Marked as polymorphic but missing PolymorphicOwner metadata");
        continue;
      }

      if (!table.Columns.Any(c => c.Name == table.PolymorphicOwner.TypeColumn))
      {
        errors.Add(
          $"{table.Name}: Polymorphic type column '{table.PolymorphicOwner.TypeColumn}' not found");
      }

      if (!table.Columns.Any(c => c.Name == table.PolymorphicOwner.IdColumn))
      {
        errors.Add(
          $"{table.Name}: Polymorphic id column '{table.PolymorphicOwner.IdColumn}' not found");
      }

      if (table.PolymorphicOwner.AllowedTypes == null || table.PolymorphicOwner.AllowedTypes.Count == 0)
      {
        warnings.Add(
          $"{table.Name}: Polymorphic table has no allowed types defined in CHECK constraint");
      }

      string typeColumn = table.PolymorphicOwner.TypeColumn;
      bool hasCheckConstraint = table.Constraints.CheckConstraints
        .Any(c => c.Expression.Contains(typeColumn));

      if (!hasCheckConstraint)
      {
        errors.Add(
          $"{table.Name}: Polymorphic table missing CHECK constraint on '{typeColumn}'");
      }
    }
  }

  /// <summary>
  /// Validates temporal tables have correct period columns and history table configuration.
  /// Per-table config overrides are honoured.
  /// </summary>
  internal static void ValidateTemporalTables(
    SchemaMetadata metadata,
    SchemaToolsConfig config,
    bool? taskOverride,
    List<string> errors,
    List<string> warnings)
  {
    ColumnNamingConfig cols = config.Columns;

    foreach (TableMetadata table in metadata.Tables.Where(t => t.HasTemporalVersioning))
    {
      SchemaToolsConfig effective = config.ResolveForTable(table.Name, table.Category);
      if (!(taskOverride ?? effective.Validation.ValidateTemporal))
      {
        continue;
      }

      ColumnMetadata? validFrom = table.Columns.FirstOrDefault(c =>
        string.Equals(c.Name, cols.ValidFrom, StringComparison.OrdinalIgnoreCase));
      if (validFrom == null)
      {
        errors.Add($"{table.Name}: Temporal table missing '{cols.ValidFrom}' column");
      }
      else if (!validFrom.IsGeneratedAlways)
      {
        errors.Add($"{table.Name}: '{cols.ValidFrom}' must be GENERATED ALWAYS AS ROW START");
      }

      ColumnMetadata? validTo = table.Columns.FirstOrDefault(c =>
        string.Equals(c.Name, cols.ValidTo, StringComparison.OrdinalIgnoreCase));
      if (validTo == null)
      {
        errors.Add($"{table.Name}: Temporal table missing '{cols.ValidTo}' column");
      }
      else if (!validTo.IsGeneratedAlways)
      {
        errors.Add($"{table.Name}: '{cols.ValidTo}' must be GENERATED ALWAYS AS ROW END");
      }

      if (string.IsNullOrEmpty(table.HistoryTable))
      {
        warnings.Add($"{table.Name}: Temporal table missing history table specification");
      }
    }
  }

  /// <summary>
  /// Validates audit column presence (created_by, updated_by) for standard tables,
  /// and correct audit structure for append-only tables.
  /// </summary>
  internal static void ValidateAuditColumnConsistency(
    SchemaMetadata metadata,
    SchemaToolsConfig config,
    bool? taskOverride,
    List<string> errors,
    List<string> warnings)
  {
    ColumnNamingConfig cols = config.Columns;

    foreach (TableMetadata table in metadata.Tables.Where(t => !t.IsAppendOnly))
    {
      SchemaToolsConfig effective = config.ResolveForTable(table.Name, table.Category);
      if (!(taskOverride ?? effective.Validation.ValidateAuditColumns))
      {
        continue;
      }

      if (!table.Columns.Any(c => string.Equals(c.Name, cols.CreatedBy, StringComparison.OrdinalIgnoreCase)))
      {
        errors.Add($"{table.Name}: Missing required '{cols.CreatedBy}' column");
      }

      if (!table.Columns.Any(c => string.Equals(c.Name, cols.UpdatedBy, StringComparison.OrdinalIgnoreCase)))
      {
        errors.Add($"{table.Name}: Missing required '{cols.UpdatedBy}' column");
      }
    }

    foreach (TableMetadata table in metadata.Tables.Where(t => t.IsAppendOnly))
    {
      SchemaToolsConfig effective = config.ResolveForTable(table.Name, table.Category);
      if (!(taskOverride ?? effective.Validation.ValidateAuditColumns))
      {
        continue;
      }

      if (!table.Columns.Any(c => string.Equals(c.Name, cols.CreatedAt, StringComparison.OrdinalIgnoreCase)))
      {
        warnings.Add($"{table.Name}: Append-only table missing '{cols.CreatedAt}' column");
      }

      if (table.Columns.Any(c => string.Equals(c.Name, cols.UpdatedBy, StringComparison.OrdinalIgnoreCase)))
      {
        warnings.Add(
          $"{table.Name}: Append-only table should not have '{cols.UpdatedBy}' column");
      }
    }
  }

  /// <summary>
  /// Validates snake_case naming conventions for tables, columns, FKs, and PKs.
  /// </summary>
  internal static void ValidateNamingConventions(SchemaMetadata metadata, List<string> warnings)
  {
    Regex snakeCasePattern = SnakeCasePattern;

    foreach (TableMetadata table in metadata.Tables)
    {
      if (!snakeCasePattern.IsMatch(table.Name))
      {
        warnings.Add(
          $"{table.Name}: Table name should be lowercase snake_case (e.g., 'table_name')");
      }

      foreach (ColumnMetadata column in table.Columns)
      {
        if (!snakeCasePattern.IsMatch(column.Name))
        {
          warnings.Add(
            $"{table.Name}.{column.Name}: Column name should be lowercase snake_case");
        }
      }

      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        string expectedPrefix = $"fk_{table.Name}_";
        if (!fk.Name.StartsWith(expectedPrefix))
        {
          warnings.Add(
            $"{table.Name}: FK '{fk.Name}' should start with '{expectedPrefix}'");
        }
      }

      if (table.Constraints.PrimaryKey != null)
      {
        string expectedName = $"pk_{table.Name}";
        if (table.Constraints.PrimaryKey.Name != expectedName)
        {
          warnings.Add(
            $"{table.Name}: PK should be named '{expectedName}', found '{table.Constraints.PrimaryKey.Name}'");
        }
      }
    }
  }

  /// <summary>
  /// Validates that every non-history table has a primary key.
  /// </summary>
  internal static void ValidatePrimaryKeys(SchemaMetadata metadata, List<string> errors)
  {
    foreach (TableMetadata table in metadata.Tables)
    {
      // Skip temporal history tables -- they do not have primary keys by design
      if (table.IsHistoryTable)
      {
        continue;
      }

      // Check either the convenience PrimaryKey property (single-column)
      // or the full Constraints.PrimaryKey (composite)
      bool hasPk = !string.IsNullOrEmpty(table.PrimaryKey)
                   || table.Constraints.PrimaryKey != null;

      if (!hasPk)
      {
        errors.Add($"{table.Name}: Table has no primary key defined");
        continue;
      }

      // For single-column PKs, validate the column exists
      if (!string.IsNullOrEmpty(table.PrimaryKey)
          && !table.Columns.Any(c => c.Name == table.PrimaryKey))
      {
        errors.Add(
          $"{table.Name}: Primary key column '{table.PrimaryKey}' not found in table");
      }
    }
  }

  /// <summary>
  /// Detects circular foreign key dependencies (excluding self-references).
  /// </summary>
  internal static void ValidateCircularForeignKeys(SchemaMetadata metadata, List<string> errors)
  {
    var dependencies = new Dictionary<string, HashSet<string>>();
    foreach (TableMetadata table in metadata.Tables)
    {
      dependencies[table.Name] = new HashSet<string>();
      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        // Ignore self-references (allowed for hierarchies)
        if (fk.ReferencedTable != table.Name)
        {
          dependencies[table.Name].Add(fk.ReferencedTable);
        }
      }
    }

    var visited = new HashSet<string>();
    var recursionStack = new HashSet<string>();

    foreach (TableMetadata table in metadata.Tables)
    {
      if (HasCycle(table.Name, dependencies, visited, recursionStack, out List<string>? cycle))
      {
        errors.Add($"Circular foreign key dependency detected: {string.Join(" -> ", cycle)}");
      }
    }
  }

  /// <summary>
  /// Validates soft-delete consistency: tables marked HasSoftDelete must have
  /// both an active column and temporal versioning.
  /// </summary>
  internal static void ValidateSoftDeleteConsistency(SchemaMetadata metadata, List<string> errors)
  {
    foreach (TableMetadata table in metadata.Tables.Where(t => t.HasSoftDelete))
    {
      if (!table.HasActiveColumn)
      {
        errors.Add($"{table.Name}: Marked as HasSoftDelete but HasActiveColumn is false");
      }

      if (!table.HasTemporalVersioning)
      {
        errors.Add($"{table.Name}: Marked as HasSoftDelete but HasTemporalVersioning is false");
      }

      // Note: We intentionally do not validate DEFAULT constraints or their values.
      // How downstream users handle active column defaults (explicit inserts, triggers,
      // application logic, etc.) is an implementation choice, not a schema requirement.
    }
  }

  /// <summary>
  /// Validates unique constraint column references and filter clause appropriateness
  /// for soft-delete tables.
  /// </summary>
  internal static void ValidateUniqueConstraints(
    SchemaMetadata metadata, SchemaToolsConfig config, List<string> errors, List<string> warnings)
  {
    foreach (TableMetadata table in metadata.Tables)
    {
      foreach (UniqueConstraint constraint in table.Constraints.UniqueConstraints)
      {
        foreach (string columnName in constraint.Columns)
        {
          if (!table.Columns.Any(c => c.Name == columnName))
          {
            errors.Add(
              $"{table.Name}: Unique constraint '{constraint.Name}' references non-existent column '{columnName}'");
          }
        }

        // Filtered unique constraints on soft-delete tables should include active column
        if (table.HasSoftDelete && !string.IsNullOrEmpty(constraint.FilterClause))
        {
          if (!constraint.FilterClause!.Contains(config.Columns.Active))
          {
            warnings.Add(
              $"{table.Name}: Filtered unique constraint '{constraint.Name}' should filter on '{config.Columns.Active} = 1' for soft delete tables");
          }
        }
      }
    }
  }

  private static bool HasCycle(
    string tableName,
    Dictionary<string, HashSet<string>> dependencies,
    HashSet<string> visited,
    HashSet<string> recursionStack,
    out List<string> cycle)
  {
    cycle = new List<string>();

    if (recursionStack.Contains(tableName))
    {
      cycle.Add(tableName);
      return true;
    }

    if (visited.Contains(tableName))
    {
      return false;
    }

    visited.Add(tableName);
    recursionStack.Add(tableName);

    if (dependencies.ContainsKey(tableName))
    {
      foreach (string dependency in dependencies[tableName])
      {
        if (HasCycle(dependency, dependencies, visited, recursionStack, out List<string>? subCycle))
        {
          cycle.Add(tableName);
          cycle.AddRange(subCycle);
          return true;
        }
      }
    }

    recursionStack.Remove(tableName);
    return false;
  }
}
