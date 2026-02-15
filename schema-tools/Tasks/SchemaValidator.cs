using System.Text.Json;
using System.Text.RegularExpressions;
using SchemaTools.Models;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Validates schema metadata for correctness and convention compliance
/// </summary>
public class SchemaValidator : MSTask
{
  [Microsoft.Build.Framework.Required]
  public string MetadataFile { get; set; } = string.Empty;

  public string ConfigFile { get; set; } = string.Empty;

  // Override validation settings
  public bool? ValidateForeignKeys { get; set; }
  public bool? ValidatePolymorphic { get; set; }
  public bool? ValidateTemporal { get; set; }
  public bool? ValidateAuditColumns { get; set; }
  public bool? EnforceNamingConventions { get; set; }
  public bool? TreatWarningsAsErrors { get; set; }

  internal SchemaToolsConfig? TestConfig { get; set; }
  internal SchemaMetadata? TestMetadata { get; set; }

  private SchemaToolsConfig _config = new();
  private readonly List<string> _errors = new();
  private readonly List<string> _warnings = new();

  private static readonly Regex SnakeCasePattern =
      new(@"^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "  Schema Validator");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      LoadConfiguration();

      SchemaMetadata? metadata = LoadMetadata();
      if (metadata == null)
      {
        return false;
      }

      Log.LogMessage($"Validating {metadata.Tables.Count} tables...");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      if (GetValidationSetting(ValidateForeignKeys, _config.Validation.ValidateForeignKeys))
        ValidateForeignKeyReferences(metadata);

      if (GetValidationSetting(ValidatePolymorphic, _config.Validation.ValidatePolymorphic))
        ValidatePolymorphicTables(metadata);

      if (GetValidationSetting(ValidateTemporal, _config.Validation.ValidateTemporal))
        ValidateTemporalTables(metadata);

      if (GetValidationSetting(ValidateAuditColumns, _config.Validation.ValidateAuditColumns))
        ValidateAuditColumnConsistency(metadata);

      if (GetValidationSetting(EnforceNamingConventions, _config.Validation.EnforceNamingConventions))
        ValidateNamingConventions(metadata);

      ValidatePrimaryKeys(metadata);
      ValidateCircularForeignKeys(metadata);
      ValidateSoftDeleteConsistency(metadata);
      ValidateUniqueConstraints(metadata);

      bool treatAsErrors = GetValidationSetting(TreatWarningsAsErrors, _config.Validation.TreatWarningsAsErrors);

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "  Validation Results");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");

      if (_warnings.Count > 0)
      {
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"! {_warnings.Count} warning(s):");
        foreach (string warning in _warnings)
        {
          if (treatAsErrors)
            Log.LogError($"  {warning}");
          else
            Log.LogWarning($"  {warning}");
        }
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
      }

      if (_errors.Count > 0)
      {
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"X {_errors.Count} error(s):");
        foreach (string error in _errors)
        {
          Log.LogError($"  {error}");
        }
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
        return false;
      }

      if (treatAsErrors && _warnings.Count > 0)
      {
        Log.LogError("Build failed due to warnings (TreatWarningsAsErrors=true)");
        return false;
      }

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          $"+ Schema validation passed: {metadata.Tables.Count} tables validated successfully");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Validation failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private void LoadConfiguration()
  {
    // Priority: TestConfig > ConfigFile > defaults
    if (TestConfig != null)
    {
      _config = TestConfig;
      Log.LogMessage("Using injected test configuration");
      return;
    }

    if (!string.IsNullOrEmpty(ConfigFile) && File.Exists(ConfigFile))
    {
      string json = File.ReadAllText(ConfigFile);
      SchemaToolsConfig? deserializedConfig = JsonSerializer.Deserialize<SchemaToolsConfig>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      _config = deserializedConfig ?? new SchemaToolsConfig();
      Log.LogMessage($"Loaded configuration from: {ConfigFile}");
    }
  }

  private SchemaMetadata? LoadMetadata()
  {
    // Priority: TestMetadata > MetadataFile
    if (TestMetadata != null)
    {
      Log.LogMessage("Using injected test metadata");
      return TestMetadata;
    }

    if (!File.Exists(MetadataFile))
    {
      Log.LogError($"Metadata file not found: {MetadataFile}");
      return null;
    }

    string json = File.ReadAllText(MetadataFile);
    SchemaMetadata? metadata = JsonSerializer.Deserialize<SchemaMetadata>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (metadata == null || metadata.Tables == null)
    {
      Log.LogError("Failed to deserialize metadata");
      return null;
    }

    return metadata;
  }

  private static bool GetValidationSetting(bool? taskParameter, bool configValue)
  {
    return taskParameter ?? configValue;
  }

  internal IReadOnlyList<string> ValidationErrors => _errors;
  internal IReadOnlyList<string> ValidationWarnings => _warnings;

  private void ValidateForeignKeyReferences(SchemaMetadata metadata)
  {
    var tableNames = new HashSet<string>(metadata.Tables.Select(t => t.Name));

    foreach (TableMetadata table in metadata.Tables)
    {
      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        if (!tableNames.Contains(fk.ReferencedTable))
        {
          _errors.Add(
              $"{table.Name}: FK '{fk.Name}' references non-existent table '{fk.ReferencedTable}'");
        }

        TableMetadata? refTable = metadata.Tables.FirstOrDefault(t => t.Name == fk.ReferencedTable);
        if (refTable != null)
        {
          foreach (string refCol in fk.ReferencedColumns)
          {
            if (!refTable.Columns.Any(c => c.Name == refCol))
            {
              _errors.Add(
                  $"{table.Name}: FK '{fk.Name}' references non-existent column '{refCol}' in table '{fk.ReferencedTable}'");
            }
          }
        }

        if (fk.Columns.Count != fk.ReferencedColumns.Count)
        {
          _errors.Add(
              $"{table.Name}: FK '{fk.Name}' has mismatched column counts ({fk.Columns.Count} vs {fk.ReferencedColumns.Count})");
        }
      }
    }
  }

  private void ValidatePolymorphicTables(SchemaMetadata metadata)
  {
    foreach (TableMetadata? table in metadata.Tables.Where(t => t.IsPolymorphic && !t.IsHistoryTable))
    {
      if (table.PolymorphicOwner == null)
      {
        _errors.Add($"{table.Name}: Marked as polymorphic but missing PolymorphicOwner metadata");
        continue;
      }

      if (!table.Columns.Any(c => c.Name == table.PolymorphicOwner.TypeColumn))
      {
        _errors.Add(
            $"{table.Name}: Polymorphic type column '{table.PolymorphicOwner.TypeColumn}' not found");
      }

      if (!table.Columns.Any(c => c.Name == table.PolymorphicOwner.IdColumn))
      {
        _errors.Add(
            $"{table.Name}: Polymorphic id column '{table.PolymorphicOwner.IdColumn}' not found");
      }

      if (table.PolymorphicOwner.AllowedTypes == null || table.PolymorphicOwner.AllowedTypes.Count == 0)
      {
        _warnings.Add(
            $"{table.Name}: Polymorphic table has no allowed types defined in CHECK constraint");
      }

      string typeColumn = table.PolymorphicOwner.TypeColumn;
      bool hasCheckConstraint = table.Constraints.CheckConstraints
          .Any(c => c.Expression.Contains(typeColumn));

      if (!hasCheckConstraint)
      {
        _errors.Add(
            $"{table.Name}: Polymorphic table missing CHECK constraint on '{typeColumn}'");
      }
    }
  }

  private void ValidateTemporalTables(SchemaMetadata metadata)
  {
    ColumnNamingConfig cols = _config.Columns;

    foreach (TableMetadata? table in metadata.Tables.Where(t => t.HasTemporalVersioning))
    {
      SchemaToolsConfig effective = _config.ResolveForTable(table.Name, table.Category);
      if (!GetValidationSetting(ValidateTemporal, effective.Validation.ValidateTemporal))
        continue;

      ColumnMetadata? validFrom = table.Columns.FirstOrDefault(c =>
          string.Equals(c.Name, cols.ValidFrom, StringComparison.OrdinalIgnoreCase));
      if (validFrom == null)
      {
        _errors.Add($"{table.Name}: Temporal table missing '{cols.ValidFrom}' column");
      }
      else if (!validFrom.IsGeneratedAlways)
      {
        _errors.Add($"{table.Name}: '{cols.ValidFrom}' must be GENERATED ALWAYS AS ROW START");
      }

      ColumnMetadata? validTo = table.Columns.FirstOrDefault(c =>
          string.Equals(c.Name, cols.ValidTo, StringComparison.OrdinalIgnoreCase));
      if (validTo == null)
      {
        _errors.Add($"{table.Name}: Temporal table missing '{cols.ValidTo}' column");
      }
      else if (!validTo.IsGeneratedAlways)
      {
        _errors.Add($"{table.Name}: '{cols.ValidTo}' must be GENERATED ALWAYS AS ROW END");
      }

      if (string.IsNullOrEmpty(table.HistoryTable))
      {
        _warnings.Add($"{table.Name}: Temporal table missing history table specification");
      }
    }
  }

  private void ValidateAuditColumnConsistency(SchemaMetadata metadata)
  {
    ColumnNamingConfig cols = _config.Columns;

    foreach (TableMetadata? table in metadata.Tables.Where(t => !t.IsAppendOnly))
    {
      SchemaToolsConfig effective = _config.ResolveForTable(table.Name, table.Category);
      if (!GetValidationSetting(ValidateAuditColumns, effective.Validation.ValidateAuditColumns))
        continue;

      if (!table.Columns.Any(c => string.Equals(c.Name, cols.CreatedBy, StringComparison.OrdinalIgnoreCase)))
      {
        _errors.Add($"{table.Name}: Missing required '{cols.CreatedBy}' column");
      }

      if (!table.Columns.Any(c => string.Equals(c.Name, cols.UpdatedBy, StringComparison.OrdinalIgnoreCase)))
      {
        _errors.Add($"{table.Name}: Missing required '{cols.UpdatedBy}' column");
      }
    }

    foreach (TableMetadata? table in metadata.Tables.Where(t => t.IsAppendOnly))
    {
      SchemaToolsConfig effective = _config.ResolveForTable(table.Name, table.Category);
      if (!GetValidationSetting(ValidateAuditColumns, effective.Validation.ValidateAuditColumns))
        continue;

      if (!table.Columns.Any(c => string.Equals(c.Name, cols.CreatedAt, StringComparison.OrdinalIgnoreCase)))
      {
        _warnings.Add($"{table.Name}: Append-only table missing '{cols.CreatedAt}' column");
      }

      if (table.Columns.Any(c => string.Equals(c.Name, cols.UpdatedBy, StringComparison.OrdinalIgnoreCase)))
      {
        _warnings.Add(
            $"{table.Name}: Append-only table should not have '{cols.UpdatedBy}' column");
      }
    }
  }

  private void ValidateNamingConventions(SchemaMetadata metadata)
  {
    Regex snakeCasePattern = SnakeCasePattern;

    foreach (TableMetadata table in metadata.Tables)
    {
      if (!snakeCasePattern.IsMatch(table.Name))
      {
        _warnings.Add(
            $"{table.Name}: Table name should be lowercase snake_case (e.g., 'table_name')");
      }

      foreach (ColumnMetadata column in table.Columns)
      {
        if (!snakeCasePattern.IsMatch(column.Name))
        {
          _warnings.Add(
              $"{table.Name}.{column.Name}: Column name should be lowercase snake_case");
        }
      }

      foreach (ForeignKeyConstraint fk in table.Constraints.ForeignKeys)
      {
        string expectedPrefix = $"fk_{table.Name}_";
        if (!fk.Name.StartsWith(expectedPrefix))
        {
          _warnings.Add(
              $"{table.Name}: FK '{fk.Name}' should start with '{expectedPrefix}'");
        }
      }

      if (table.Constraints.PrimaryKey != null)
      {
        string expectedName = $"pk_{table.Name}";
        if (table.Constraints.PrimaryKey.Name != expectedName)
        {
          _warnings.Add(
              $"{table.Name}: PK should be named '{expectedName}', found '{table.Constraints.PrimaryKey.Name}'");
        }
      }
    }
  }

  private void ValidatePrimaryKeys(SchemaMetadata metadata)
  {
    foreach (TableMetadata table in metadata.Tables)
    {
      // Skip temporal history tables - they do not have primary keys by design
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
        _errors.Add($"{table.Name}: Table has no primary key defined");
        continue;
      }

      // For single-column PKs, validate the column exists
      if (!string.IsNullOrEmpty(table.PrimaryKey)
          && !table.Columns.Any(c => c.Name == table.PrimaryKey))
      {
        _errors.Add(
            $"{table.Name}: Primary key column '{table.PrimaryKey}' not found in table");
      }
    }
  }

  private void ValidateCircularForeignKeys(SchemaMetadata metadata)
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
        _errors.Add($"Circular foreign key dependency detected: {string.Join(" -> ", cycle)}");
      }
    }
  }

  private static bool HasCycle(string tableName, Dictionary<string, HashSet<string>> dependencies,
      HashSet<string> visited, HashSet<string> recursionStack, out List<string> cycle)
  {
    cycle = new List<string>();

    if (recursionStack.Contains(tableName))
    {
      cycle.Add(tableName);
      return true;
    }

    if (visited.Contains(tableName))
      return false;

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

  private void ValidateSoftDeleteConsistency(SchemaMetadata metadata)
  {
    foreach (TableMetadata? table in metadata.Tables.Where(t => t.HasSoftDelete))
    {
      if (!table.HasActiveColumn)
      {
        _errors.Add($"{table.Name}: Marked as HasSoftDelete but HasActiveColumn is false");
      }

      if (!table.HasTemporalVersioning)
      {
        _errors.Add($"{table.Name}: Marked as HasSoftDelete but HasTemporalVersioning is false");
      }

      // Note: We intentionally do not validate DEFAULT constraints or their values.
      // How downstream users handle active column defaults (explicit inserts, triggers,
      // application logic, etc.) is an implementation choice, not a schema requirement.
    }
  }

  private void ValidateUniqueConstraints(SchemaMetadata metadata)
  {
    foreach (TableMetadata table in metadata.Tables)
    {
      foreach (UniqueConstraint constraint in table.Constraints.UniqueConstraints)
      {
        foreach (string columnName in constraint.Columns)
        {
          if (!table.Columns.Any(c => c.Name == columnName))
          {
            _errors.Add(
                $"{table.Name}: Unique constraint '{constraint.Name}' references non-existent column '{columnName}'");
          }
        }

        // Filtered unique constraints on soft-delete tables should include active column
        if (table.HasSoftDelete && !string.IsNullOrEmpty(constraint.FilterClause))
        {
          if (!constraint.FilterClause!.Contains(_config.Columns.Active))
          {
            _warnings.Add(
                $"{table.Name}: Filtered unique constraint '{constraint.Name}' should filter on '{_config.Columns.Active} = 1' for soft delete tables");
          }
        }
      }
    }
  }
}
