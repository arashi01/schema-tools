using System.Text.Json.Serialization;
using SchemaTools.Models;

namespace SchemaTools.Configuration;

/// <summary>
/// Per-project configuration for schema tools
/// </summary>
public class SchemaToolsConfig
{
  [JsonPropertyName("database")]
  public string Database { get; set; } = string.Empty;

  [JsonPropertyName("defaultSchema")]
  public string DefaultSchema { get; set; } = SchemaToolsDefaults.DefaultSchema;

  [JsonPropertyName("sqlServerVersion")]
  public SqlServerVersion SqlServerVersion { get; set; } = SqlServerVersion.Sql170;

  [JsonPropertyName("features")]
  public FeatureConfig Features { get; set; } = new();

  [JsonPropertyName("validation")]
  public ValidationConfig Validation { get; set; } = new();

  [JsonPropertyName("documentation")]
  public DocumentationConfig Documentation { get; set; } = new();

  [JsonPropertyName("columns")]
  public ColumnNamingConfig Columns { get; set; } = new();

  [JsonPropertyName("purge")]
  public PurgeConfig Purge { get; set; } = new();

  [JsonPropertyName("views")]
  public ViewsConfig Views { get; set; } = new();

  [JsonPropertyName("categories")]
  public Dictionary<string, string> Categories { get; set; } = new();

  /// <summary>
  /// Per-table or per-category configuration overrides.
  /// Keys are exact table names or glob patterns (e.g. "audit_*").
  /// </summary>
  [JsonPropertyName("overrides")]
  public Dictionary<string, TableOverrideConfig> Overrides { get; set; } = new();

  /// <summary>
  /// Returns the effective config for a specific table by merging global settings
  /// with any matching overrides (category match first, then table name, then glob).
  /// </summary>
  internal SchemaToolsConfig ResolveForTable(string tableName, string? category)
  {
    var matchingOverrides = new List<TableOverrideConfig>();

    foreach (KeyValuePair<string, TableOverrideConfig> kvp in Overrides)
    {
      string pattern = kvp.Key;
      if (MatchesTableOrCategory(pattern, tableName, category))
      {
        matchingOverrides.Add(kvp.Value);
      }
    }

    if (matchingOverrides.Count == 0)
      return this;

    // Clone the base config and apply overrides in order
    SchemaToolsConfig effective = ShallowClone();
    foreach (TableOverrideConfig over in matchingOverrides)
    {
      effective.Features = MergeFeatures(effective.Features, over.Features);
      effective.Validation = MergeValidation(effective.Validation, over.Validation);
    }

    return effective;
  }

  private static bool MatchesTableOrCategory(string pattern, string tableName, string? category)
  {
    // Exact table name match
    if (string.Equals(pattern, tableName, StringComparison.OrdinalIgnoreCase))
      return true;

    // Category match (prefixed with "category:")
    const string categoryPrefix = "category:";
    if (pattern.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(category))
    {
      string categoryValue = pattern[categoryPrefix.Length..].Trim();
      if (string.Equals(categoryValue, category, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    // Glob match (simple * wildcard)
    if (pattern.Contains("*"))
    {
      string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
          .Replace("\\*", ".*") + "$";
      return System.Text.RegularExpressions.Regex.IsMatch(
          tableName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    return false;
  }

  private SchemaToolsConfig ShallowClone()
  {
    return new SchemaToolsConfig
    {
      Database = Database,
      DefaultSchema = DefaultSchema,
      SqlServerVersion = SqlServerVersion,
      Features = new FeatureConfig
      {
        EnableSoftDelete = Features.EnableSoftDelete,
        EnableTemporalVersioning = Features.EnableTemporalVersioning,
        SoftDeleteMode = Features.SoftDeleteMode,
        GenerateReactivationGuards = Features.GenerateReactivationGuards,
        ReactivationCascade = Features.ReactivationCascade,
        ReactivationCascadeToleranceMs = Features.ReactivationCascadeToleranceMs,
        DetectPolymorphicPatterns = Features.DetectPolymorphicPatterns,
        DetectAppendOnlyTables = Features.DetectAppendOnlyTables
      },
      Validation = new ValidationConfig
      {
        ValidateForeignKeys = Validation.ValidateForeignKeys,
        ValidatePolymorphic = Validation.ValidatePolymorphic,
        ValidateTemporal = Validation.ValidateTemporal,
        ValidateAuditColumns = Validation.ValidateAuditColumns,
        EnforceNamingConventions = Validation.EnforceNamingConventions,
        TreatWarningsAsErrors = Validation.TreatWarningsAsErrors
      },
      Documentation = Documentation,
      Columns = Columns,
      Purge = Purge,
      Views = Views,
      Categories = Categories,
      Overrides = Overrides
    };
  }

  private static FeatureConfig MergeFeatures(FeatureConfig baseConfig, FeatureOverrideConfig? over)
  {
    if (over == null)
      return baseConfig;
    return new FeatureConfig
    {
      EnableSoftDelete = over.EnableSoftDelete ?? baseConfig.EnableSoftDelete,
      EnableTemporalVersioning = over.EnableTemporalVersioning ?? baseConfig.EnableTemporalVersioning,
      SoftDeleteMode = over.SoftDeleteMode ?? baseConfig.SoftDeleteMode,
      GenerateReactivationGuards = over.GenerateReactivationGuards ?? baseConfig.GenerateReactivationGuards,
      ReactivationCascade = over.ReactivationCascade ?? baseConfig.ReactivationCascade,
      ReactivationCascadeToleranceMs = over.ReactivationCascadeToleranceMs ?? baseConfig.ReactivationCascadeToleranceMs,
      DetectPolymorphicPatterns = over.DetectPolymorphicPatterns ?? baseConfig.DetectPolymorphicPatterns,
      DetectAppendOnlyTables = over.DetectAppendOnlyTables ?? baseConfig.DetectAppendOnlyTables
    };
  }

  private static ValidationConfig MergeValidation(ValidationConfig baseConfig, ValidationOverrideConfig? over)
  {
    if (over == null)
      return baseConfig;
    return new ValidationConfig
    {
      ValidateForeignKeys = over.ValidateForeignKeys ?? baseConfig.ValidateForeignKeys,
      ValidatePolymorphic = over.ValidatePolymorphic ?? baseConfig.ValidatePolymorphic,
      ValidateTemporal = over.ValidateTemporal ?? baseConfig.ValidateTemporal,
      ValidateAuditColumns = over.ValidateAuditColumns ?? baseConfig.ValidateAuditColumns,
      EnforceNamingConventions = over.EnforceNamingConventions ?? baseConfig.EnforceNamingConventions,
      TreatWarningsAsErrors = over.TreatWarningsAsErrors ?? baseConfig.TreatWarningsAsErrors
    };
  }
}

public class FeatureConfig
{
  [JsonPropertyName("enableSoftDelete")]
  public bool EnableSoftDelete { get; set; } = true;

  [JsonPropertyName("enableTemporalVersioning")]
  public bool EnableTemporalVersioning { get; set; } = true;

  /// <summary>
  /// Default soft-delete mode for parent tables.
  /// Can be overridden per-table via config overrides.
  /// </summary>
  [JsonPropertyName("softDeleteMode")]
  public SoftDeleteMode SoftDeleteMode { get; set; } = SoftDeleteMode.Cascade;

  /// <summary>
  /// Generate reactivation guard triggers for child tables.
  /// These prevent reactivating a child when its parent is inactive.
  /// </summary>
  [JsonPropertyName("generateReactivationGuards")]
  public bool GenerateReactivationGuards { get; set; } = true;

  [JsonPropertyName("detectPolymorphicPatterns")]
  public bool DetectPolymorphicPatterns { get; set; } = true;

  [JsonPropertyName("detectAppendOnlyTables")]
  public bool DetectAppendOnlyTables { get; set; } = true;

  /// <summary>
  /// Enable reactivation cascade for parent tables (default: false).
  /// When enabled via per-table overrides, reactivating a parent will
  /// auto-reactivate children that were soft-deleted at the same time.
  /// </summary>
  [JsonPropertyName("reactivationCascade")]
  public bool ReactivationCascade { get; set; } = false;

  /// <summary>
  /// Tolerance in milliseconds for matching child soft-delete timestamps to the parent
  /// during reactivation cascade. Only children whose valid_to is within this tolerance
  /// of the parent's valid_to are reactivated.
  /// </summary>
  [JsonPropertyName("reactivationCascadeToleranceMs")]
  public int ReactivationCascadeToleranceMs { get; set; } = SchemaToolsDefaults.ReactivationCascadeToleranceMs;
}

public class ValidationConfig
{
  [JsonPropertyName("validateForeignKeys")]
  public bool ValidateForeignKeys { get; set; } = true;

  [JsonPropertyName("validatePolymorphic")]
  public bool ValidatePolymorphic { get; set; } = true;

  [JsonPropertyName("validateTemporal")]
  public bool ValidateTemporal { get; set; } = true;

  [JsonPropertyName("validateAuditColumns")]
  public bool ValidateAuditColumns { get; set; } = true;

  [JsonPropertyName("enforceNamingConventions")]
  public bool EnforceNamingConventions { get; set; } = true;

  [JsonPropertyName("treatWarningsAsErrors")]
  public bool TreatWarningsAsErrors { get; set; } = false;
}

public class DocumentationConfig
{
  [JsonPropertyName("enabled")]
  public bool Enabled { get; set; } = true;

  [JsonPropertyName("includeErDiagrams")]
  public bool IncludeErDiagrams { get; set; } = true;

  [JsonPropertyName("includeStatistics")]
  public bool IncludeStatistics { get; set; } = true;

  [JsonPropertyName("includeConstraints")]
  public bool IncludeConstraints { get; set; } = true;

  [JsonPropertyName("includeIndexes")]
  public bool IncludeIndexes { get; set; } = false;

  /// <summary>
  /// Controls how temporal history tables appear in generated documentation.
  /// </summary>
  [JsonPropertyName("historyTables")]
  public HistoryTableMode HistoryTables { get; set; } = HistoryTableMode.None;

  /// <summary>
  /// When enabled, infrastructure columns (soft-delete flags, temporal period columns,
  /// audit trail columns) are visually grouped and styled distinctly from domain columns.
  /// </summary>
  [JsonPropertyName("infrastructureColumnStyling")]
  public bool InfrastructureColumnStyling { get; set; } = true;

  /// <summary>
  /// When enabled, ER diagrams show only domain columns, excluding infrastructure
  /// columns (soft-delete, temporal, audit) for diagram clarity.
  /// </summary>
  [JsonPropertyName("erDiagramDomainColumnsOnly")]
  public bool ErDiagramDomainColumnsOnly { get; set; } = true;
}

/// <summary>
/// Column naming conventions for pattern detection (matched case-insensitively).
/// </summary>
public class ColumnNamingConfig
{
  [JsonPropertyName("active")]
  public string Active { get; set; } = SchemaToolsDefaults.ActiveColumn;

  /// <summary>
  /// SQL literal for active state (used in generated triggers and views).
  /// </summary>
  [JsonPropertyName("activeValue")]
  public string ActiveValue { get; set; } = SchemaToolsDefaults.ActiveValue;

  /// <summary>
  /// SQL literal for inactive/deleted state (used in generated triggers and views).
  /// </summary>
  [JsonPropertyName("inactiveValue")]
  public string InactiveValue { get; set; } = SchemaToolsDefaults.InactiveValue;

  [JsonPropertyName("createdAt")]
  public string CreatedAt { get; set; } = SchemaToolsDefaults.CreatedAtColumn;

  [JsonPropertyName("createdBy")]
  public string CreatedBy { get; set; } = SchemaToolsDefaults.CreatedByColumn;

  [JsonPropertyName("updatedBy")]
  public string UpdatedBy { get; set; } = SchemaToolsDefaults.UpdatedByColumn;

  /// <summary>
  /// SQL data type for the updated_by column (used in generated triggers).
  /// </summary>
  [JsonPropertyName("updatedByType")]
  public string UpdatedByType { get; set; } = SchemaToolsDefaults.UpdatedByType;

  [JsonPropertyName("validFrom")]
  public string ValidFrom { get; set; } = SchemaToolsDefaults.ValidFromColumn;

  [JsonPropertyName("validTo")]
  public string ValidTo { get; set; } = SchemaToolsDefaults.ValidToColumn;

  /// <summary>
  /// Table that audit columns (created_by, updated_by) reference as a foreign key.
  /// Leave empty if not applicable.
  /// </summary>
  [JsonPropertyName("auditForeignKeyTable")]
  public string AuditForeignKeyTable { get; set; } = string.Empty;

  /// <summary>
  /// Polymorphic type/ID column pairs for detection.
  /// Empty by default; add patterns as needed.
  /// </summary>
  [JsonPropertyName("polymorphicPatterns")]
  public List<PolymorphicPatternConfig> PolymorphicPatterns { get; set; } = new();
}

public class PurgeConfig
{
  [JsonPropertyName("enabled")]
  public bool Enabled { get; set; } = true;

  [JsonPropertyName("procedureName")]
  public string ProcedureName { get; set; } = SchemaToolsDefaults.PurgeProcedureName;

  [JsonPropertyName("defaultGracePeriodDays")]
  public int DefaultGracePeriodDays { get; set; } = SchemaToolsDefaults.DefaultGracePeriodDays;

  [JsonPropertyName("defaultBatchSize")]
  public int DefaultBatchSize { get; set; } = SchemaToolsDefaults.DefaultBatchSize;
}

public class PolymorphicPatternConfig
{
  [JsonPropertyName("typeColumn")]
  public string TypeColumn { get; set; } = string.Empty;

  [JsonPropertyName("idColumn")]
  public string IdColumn { get; set; } = string.Empty;
}

/// <summary>
/// Per-table or per-category configuration override.
/// Only non-null properties take effect; null properties inherit from global config.
/// </summary>
public class TableOverrideConfig
{
  [JsonPropertyName("features")]
  public FeatureOverrideConfig? Features { get; set; }

  [JsonPropertyName("validation")]
  public ValidationOverrideConfig? Validation { get; set; }
}

/// <summary>
/// Nullable feature overrides - null means inherit from global config
/// </summary>
public class FeatureOverrideConfig
{
  [JsonPropertyName("enableSoftDelete")]
  public bool? EnableSoftDelete { get; set; }

  [JsonPropertyName("enableTemporalVersioning")]
  public bool? EnableTemporalVersioning { get; set; }

  /// <summary>
  /// Override the soft-delete mode for this table.
  /// </summary>
  [JsonPropertyName("softDeleteMode")]
  public SoftDeleteMode? SoftDeleteMode { get; set; }

  [JsonPropertyName("generateReactivationGuards")]
  public bool? GenerateReactivationGuards { get; set; }
  /// <summary>
  /// Enable reactivation cascade for this table.
  /// When true, reactivating this parent will also reactivate children that
  /// were soft-deleted within the configured tolerance of the parent.
  /// </summary>
  [JsonPropertyName("reactivationCascade")]
  public bool? ReactivationCascade { get; set; }

  [JsonPropertyName("reactivationCascadeToleranceMs")]
  public int? ReactivationCascadeToleranceMs { get; set; }

  [JsonPropertyName("detectPolymorphicPatterns")]
  public bool? DetectPolymorphicPatterns { get; set; }

  [JsonPropertyName("detectAppendOnlyTables")]
  public bool? DetectAppendOnlyTables { get; set; }
}

/// <summary>
/// Active-record views configuration for soft-delete tables.
/// Generated views filter to active records only, eliminating the need for
/// WHERE active = 1 in application queries.
/// </summary>
public class ViewsConfig
{
  [JsonPropertyName("enabled")]
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Naming pattern for generated views. {table} is replaced with the table name.
  /// </summary>
  [JsonPropertyName("namingPattern")]
  public string NamingPattern { get; set; } = SchemaToolsDefaults.ViewNamingPattern;

  [JsonPropertyName("includeDeletedViews")]
  public bool IncludeDeletedViews { get; set; } = false;

  /// <summary>
  /// Naming pattern for deleted-record views. {table} is replaced with the table name.
  /// </summary>
  [JsonPropertyName("deletedViewNamingPattern")]
  public string DeletedViewNamingPattern { get; set; } = SchemaToolsDefaults.DeletedViewNamingPattern;
}

/// <summary>
/// Nullable validation overrides -- null means inherit from global config
/// </summary>
public class ValidationOverrideConfig
{
  [JsonPropertyName("validateForeignKeys")]
  public bool? ValidateForeignKeys { get; set; }

  [JsonPropertyName("validatePolymorphic")]
  public bool? ValidatePolymorphic { get; set; }

  [JsonPropertyName("validateTemporal")]
  public bool? ValidateTemporal { get; set; }

  [JsonPropertyName("validateAuditColumns")]
  public bool? ValidateAuditColumns { get; set; }

  [JsonPropertyName("enforceNamingConventions")]
  public bool? EnforceNamingConventions { get; set; }

  [JsonPropertyName("treatWarningsAsErrors")]
  public bool? TreatWarningsAsErrors { get; set; }
}
