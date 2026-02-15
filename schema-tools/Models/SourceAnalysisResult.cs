namespace SchemaTools.Models;

/// <summary>
/// Pre-build source analysis result - used for code generation decisions.
/// Produced by SchemaSourceAnalyser, consumed by SqlTriggerGenerator,
/// SqlProcedureGenerator, and SqlViewGenerator.
/// </summary>
public class SourceAnalysisResult
{
  public string Version { get; set; } = SchemaToolsDefaults.MetadataVersion;
  public DateTime AnalysedAt { get; set; }
  public string SqlServerVersion { get; set; } = SchemaToolsDefaults.SqlServerVersion;
  public string DefaultSchema { get; set; } = SchemaToolsDefaults.DefaultSchema;
  public List<TableAnalysis> Tables { get; set; } = new();

  /// <summary>
  /// Column configuration used for trigger generation
  /// </summary>
  public ColumnConfig Columns { get; set; } = new();

  /// <summary>
  /// Feature configuration flags
  /// </summary>
  public FeatureFlags Features { get; set; } = new();

  /// <summary>
  /// Existing triggers discovered in the project (for explicit-wins policy)
  /// </summary>
  public List<ExistingTrigger> ExistingTriggers { get; set; } = new();

  /// <summary>
  /// Existing views discovered in the project (for explicit-wins policy)
  /// </summary>
  public List<ExistingView> ExistingViews { get; set; } = new();

  /// <summary>
  /// The generated triggers directory path (used to distinguish explicit vs generated)
  /// </summary>
  public string GeneratedTriggersDirectory { get; set; } = string.Empty;

  /// <summary>
  /// The generated views directory path (used to distinguish explicit vs generated)
  /// </summary>
  public string GeneratedViewsDirectory { get; set; } = string.Empty;
}

/// <summary>
/// Column configuration for code generation
/// </summary>
public class ColumnConfig
{
  public string Active { get; set; } = SchemaToolsDefaults.ActiveColumn;
  public string ActiveValue { get; set; } = SchemaToolsDefaults.ActiveValue;
  public string InactiveValue { get; set; } = SchemaToolsDefaults.InactiveValue;
  public string UpdatedBy { get; set; } = SchemaToolsDefaults.UpdatedByColumn;
  public string UpdatedByType { get; set; } = SchemaToolsDefaults.UpdatedByType;
  public string ValidFrom { get; set; } = SchemaToolsDefaults.ValidFromColumn;
  public string ValidTo { get; set; } = SchemaToolsDefaults.ValidToColumn;
}

/// <summary>
/// Feature flags for code generation
/// </summary>
public class FeatureFlags
{
  public bool GenerateReactivationGuards { get; set; } = true;
}

/// <summary>
/// Represents an existing trigger discovered during source analysis
/// </summary>
public class ExistingTrigger
{
  public string Name { get; set; } = string.Empty;
  public string Schema { get; set; } = SchemaToolsDefaults.DefaultSchema;
  public string TargetTable { get; set; } = string.Empty;
  public string SourceFile { get; set; } = string.Empty;

  /// <summary>
  /// True if this trigger is in the _generated directory (owned by SchemaTools)
  /// </summary>
  public bool IsGenerated { get; set; }
}

/// <summary>
/// Represents an existing view discovered during source analysis
/// </summary>
public class ExistingView
{
  public string Name { get; set; } = string.Empty;
  public string Schema { get; set; } = SchemaToolsDefaults.DefaultSchema;
  public string SourceFile { get; set; } = string.Empty;

  /// <summary>
  /// True if this view is in the _generated directory (owned by SchemaTools)
  /// </summary>
  public bool IsGenerated { get; set; }
}

/// <summary>
/// Analysis result for a single table - focused on code generation needs
/// </summary>
public class TableAnalysis
{
  public string Name { get; set; } = string.Empty;
  public string Schema { get; set; } = SchemaToolsDefaults.DefaultSchema;
  public string? Category { get; set; }
  public string SourceFile { get; set; } = string.Empty;

  // Soft-delete pattern detection
  public bool HasActiveColumn { get; set; }
  public bool HasTemporalVersioning { get; set; }
  public bool HasSoftDelete { get; set; }
  public string? ActiveColumnName { get; set; }

  /// <summary>
  /// History table for temporal versioning (e.g. "[dbo].[users_history]")
  /// </summary>
  public string? HistoryTable { get; set; }

  /// <summary>
  /// Temporal period start column name (e.g. "valid_from")
  /// </summary>
  public string? ValidFromColumn { get; set; }

  /// <summary>
  /// Temporal period end column name (e.g. "valid_to")
  /// </summary>
  public string? ValidToColumn { get; set; }

  /// <summary>
  /// Soft-delete trigger mode for this table.
  /// Determines whether cascade, restrict, or ignore behaviour is used.
  /// </summary>
  public SoftDeleteMode SoftDeleteMode { get; set; } = SoftDeleteMode.Cascade;

  /// <summary>
  /// Whether reactivation cascade is enabled for this table.
  /// When true, reactivating this parent will auto-reactivate children
  /// that were soft-deleted at the same time.
  /// </summary>
  public bool ReactivationCascade { get; set; } = false;

  /// <summary>
  /// Tolerance in milliseconds for matching child soft-delete timestamps
  /// during reactivation cascade.
  /// </summary>
  public int ReactivationCascadeToleranceMs { get; set; } = SchemaToolsDefaults.ReactivationCascadeToleranceMs;

  // Keys and relationships
  public List<string> PrimaryKeyColumns { get; set; } = new();
  public List<ForeignKeyRef> ForeignKeyReferences { get; set; } = new();

  // FK graph (computed)
  public List<string> ChildTables { get; set; } = new();
  public bool IsLeafTable { get; set; }
}

/// <summary>
/// Foreign key reference for dependency analysis
/// </summary>
public class ForeignKeyRef
{
  public string ReferencedTable { get; set; } = string.Empty;
  public string ReferencedSchema { get; set; } = SchemaToolsDefaults.DefaultSchema;
  public List<string> Columns { get; set; } = new();
  public List<string> ReferencedColumns { get; set; } = new();
  public string OnDelete { get; set; } = SchemaToolsDefaults.ForeignKeyOnDeleteDefault;
}
