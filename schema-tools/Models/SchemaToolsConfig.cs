using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// Soft-delete trigger mode for parent tables.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SoftDeleteMode
{
  /// <summary>
  /// Cascade soft-delete to children automatically (default).
  /// When parent.active = 0, all children.active = 0.
  /// </summary>
  Cascade,

  /// <summary>
  /// Restrict soft-delete if active children exist.
  /// Must soft-delete children first before parent.
  /// </summary>
  Restrict,

  /// <summary>
  /// Ignore - no trigger generated for this table.
  /// Table excluded from soft-delete handling entirely.
  /// </summary>
  Ignore
}

/// <summary>
/// Per-project configuration for schema tools
/// </summary>
public class SchemaToolsConfig
{
  /// <summary>
  /// Database name
  /// </summary>
  [JsonPropertyName("database")]
  public string Database { get; set; } = string.Empty;

  /// <summary>
  /// Default schema name
  /// </summary>
  [JsonPropertyName("defaultSchema")]
  public string DefaultSchema { get; set; } = "dbo";

  /// <summary>
  /// SQL Server version for parser
  /// </summary>
  [JsonPropertyName("sqlServerVersion")]
  public string SqlServerVersion { get; set; } = "Sql160";

  /// <summary>
  /// Feature flags
  /// </summary>
  [JsonPropertyName("features")]
  public FeatureConfig Features { get; set; } = new();

  /// <summary>
  /// Validation settings
  /// </summary>
  [JsonPropertyName("validation")]
  public ValidationConfig Validation { get; set; } = new();

  /// <summary>
  /// Documentation settings
  /// </summary>
  [JsonPropertyName("documentation")]
  public DocumentationConfig Documentation { get; set; } = new();

  /// <summary>
  /// Column naming conventions used for pattern detection
  /// </summary>
  [JsonPropertyName("columns")]
  public ColumnNamingConfig Columns { get; set; } = new();

  /// <summary>
  /// Purge procedure configuration
  /// </summary>
  [JsonPropertyName("purge")]
  public PurgeConfig Purge { get; set; } = new();

  /// <summary>
  /// Custom category descriptions
  /// </summary>
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
#if NETSTANDARD2_0
      string categoryValue = pattern.Substring(categoryPrefix.Length).Trim();
#else
      string categoryValue = pattern[categoryPrefix.Length..].Trim();
#endif
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
  /// <summary>
  /// Enable soft delete pattern (temporal + active column)
  /// </summary>
  [JsonPropertyName("enableSoftDelete")]
  public bool EnableSoftDelete { get; set; } = true;

  /// <summary>
  /// Enable temporal versioning detection
  /// </summary>
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

  /// <summary>
  /// Detect and document polymorphic patterns
  /// </summary>
  [JsonPropertyName("detectPolymorphicPatterns")]
  public bool DetectPolymorphicPatterns { get; set; } = true;

  /// <summary>
  /// Detect and document append-only tables
  /// </summary>
  [JsonPropertyName("detectAppendOnlyTables")]
  public bool DetectAppendOnlyTables { get; set; } = true;
}

public class ValidationConfig
{
  /// <summary>
  /// Validate foreign key references
  /// </summary>
  [JsonPropertyName("validateForeignKeys")]
  public bool ValidateForeignKeys { get; set; } = true;

  /// <summary>
  /// Validate polymorphic table consistency
  /// </summary>
  [JsonPropertyName("validatePolymorphic")]
  public bool ValidatePolymorphic { get; set; } = true;

  /// <summary>
  /// Validate temporal table structure
  /// </summary>
  [JsonPropertyName("validateTemporal")]
  public bool ValidateTemporal { get; set; } = true;

  /// <summary>
  /// Validate audit column consistency
  /// </summary>
  [JsonPropertyName("validateAuditColumns")]
  public bool ValidateAuditColumns { get; set; } = true;

  /// <summary>
  /// Enforce naming conventions
  /// </summary>
  [JsonPropertyName("enforceNamingConventions")]
  public bool EnforceNamingConventions { get; set; } = true;

  /// <summary>
  /// Treat warnings as errors
  /// </summary>
  [JsonPropertyName("treatWarningsAsErrors")]
  public bool TreatWarningsAsErrors { get; set; } = false;
}

public class DocumentationConfig
{
  /// <summary>
  /// Generate documentation
  /// </summary>
  [JsonPropertyName("enabled")]
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Include ER diagrams (Mermaid)
  /// </summary>
  [JsonPropertyName("includeErDiagrams")]
  public bool IncludeErDiagrams { get; set; } = true;

  /// <summary>
  /// Include statistics
  /// </summary>
  [JsonPropertyName("includeStatistics")]
  public bool IncludeStatistics { get; set; } = true;

  /// <summary>
  /// Include constraints
  /// </summary>
  [JsonPropertyName("includeConstraints")]
  public bool IncludeConstraints { get; set; } = true;

  /// <summary>
  /// Include indexes
  /// </summary>
  [JsonPropertyName("includeIndexes")]
  public bool IncludeIndexes { get; set; } = false;

  /// <summary>
  /// Output format
  /// </summary>
  [JsonPropertyName("format")]
  public string Format { get; set; } = "markdown";
}

/// <summary>
/// Column naming conventions for pattern detection.
/// All names are matched case-insensitively.
/// </summary>
public class ColumnNamingConfig
{
  /// <summary>
  /// Soft-delete active column name
  /// </summary>
  [JsonPropertyName("active")]
  public string Active { get; set; } = "active";

  /// <summary>
  /// Value indicating active state (used in generated SQL)
  /// </summary>
  [JsonPropertyName("activeValue")]
  public string ActiveValue { get; set; } = "1";

  /// <summary>
  /// Value indicating inactive/deleted state (used in generated SQL)
  /// </summary>
  [JsonPropertyName("inactiveValue")]
  public string InactiveValue { get; set; } = "0";

  /// <summary>
  /// Append-only timestamp column name
  /// </summary>
  [JsonPropertyName("createdAt")]
  public string CreatedAt { get; set; } = "created_at";

  /// <summary>
  /// Audit column: created by
  /// </summary>
  [JsonPropertyName("createdBy")]
  public string CreatedBy { get; set; } = "created_by";

  /// <summary>
  /// Audit column: updated by
  /// </summary>
  [JsonPropertyName("updatedBy")]
  public string UpdatedBy { get; set; } = "updated_by";

  /// <summary>
  /// SQL data type for the updated_by column (used in generated triggers)
  /// </summary>
  [JsonPropertyName("updatedByType")]
  public string UpdatedByType { get; set; } = "UNIQUEIDENTIFIER";

  /// <summary>
  /// Temporal period start column name
  /// </summary>
  [JsonPropertyName("validFrom")]
  public string ValidFrom { get; set; } = "valid_from";

  /// <summary>
  /// Temporal period end column name
  /// </summary>
  [JsonPropertyName("validTo")]
  public string ValidTo { get; set; } = "valid_to";

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

/// <summary>
/// Purge procedure configuration
/// </summary>
public class PurgeConfig
{
  /// <summary>
  /// Whether to generate the purge procedure
  /// </summary>
  [JsonPropertyName("enabled")]
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Name of the generated purge procedure
  /// </summary>
  [JsonPropertyName("procedureName")]
  public string ProcedureName { get; set; } = "usp_purge_soft_deleted";

  /// <summary>
  /// Default grace period in days before soft-deleted records can be purged
  /// </summary>
  [JsonPropertyName("defaultGracePeriodDays")]
  public int DefaultGracePeriodDays { get; set; } = 90;

  /// <summary>
  /// Default batch size for purge operations (0 = unlimited)
  /// </summary>
  [JsonPropertyName("defaultBatchSize")]
  public int DefaultBatchSize { get; set; } = 1000;
}

/// <summary>
/// A single polymorphic type/ID column pair
/// </summary>
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

  /// <summary>
  /// Override reactivation guard generation for this table.
  /// </summary>
  [JsonPropertyName("generateReactivationGuards")]
  public bool? GenerateReactivationGuards { get; set; }

  [JsonPropertyName("detectPolymorphicPatterns")]
  public bool? DetectPolymorphicPatterns { get; set; }

  [JsonPropertyName("detectAppendOnlyTables")]
  public bool? DetectAppendOnlyTables { get; set; }
}

/// <summary>
/// Nullable validation overrides — null means inherit from global config
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
