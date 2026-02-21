namespace SchemaTools.Models;

/// <summary>
/// Pre-build source analysis result - used for code generation decisions.
/// Produced by SchemaSourceAnalyser, consumed by SqlTriggerGenerator,
/// SqlProcedureGenerator, and SqlViewGenerator.
/// </summary>
public sealed record SourceAnalysisResult
{
  public string ToolVersion { get; init; } = SchemaToolsDefaults.MetadataVersion;
  public DateTime AnalysedAt { get; init; }
  public SqlServerVersion SqlServerVersion { get; init; } = SqlServerVersion.Sql170;
  public string DefaultSchema { get; init; } = SchemaToolsDefaults.DefaultSchema;
  public IReadOnlyList<TableAnalysis> Tables { get; init; } = [];
  public ColumnConfig Columns { get; init; } = new();
  public FeatureFlags Features { get; init; } = new();

  /// <summary>
  /// Existing triggers discovered in the project (for explicit-wins policy).
  /// </summary>
  public IReadOnlyList<ExistingTrigger> ExistingTriggers { get; init; } = [];

  /// <summary>
  /// Existing views discovered in the project (for explicit-wins policy).
  /// </summary>
  public IReadOnlyList<ExistingView> ExistingViews { get; init; } = [];

  public string GeneratedTriggersDirectory { get; init; } = string.Empty;
  public string GeneratedViewsDirectory { get; init; } = string.Empty;
}

public sealed record ColumnConfig
{
  public string Active { get; init; } = SchemaToolsDefaults.ActiveColumn;
  public string ActiveValue { get; init; } = SchemaToolsDefaults.ActiveValue;
  public string InactiveValue { get; init; } = SchemaToolsDefaults.InactiveValue;
  public string UpdatedBy { get; init; } = SchemaToolsDefaults.UpdatedByColumn;
  public string UpdatedByType { get; init; } = SchemaToolsDefaults.UpdatedByType;
  public string ValidFrom { get; init; } = SchemaToolsDefaults.ValidFromColumn;
  public string ValidTo { get; init; } = SchemaToolsDefaults.ValidToColumn;
}

public sealed record FeatureFlags
{
  public bool GenerateReactivationGuards { get; init; } = true;
}

public sealed record ExistingTrigger
{
  public required string Name { get; init; }
  public string Schema { get; init; } = SchemaToolsDefaults.DefaultSchema;
  public required string TargetTable { get; init; }
  public required string SourceFile { get; init; }
  public bool IsGenerated { get; init; }
}

public sealed record ExistingView
{
  public required string Name { get; init; }
  public string Schema { get; init; } = SchemaToolsDefaults.DefaultSchema;
  public required string SourceFile { get; init; }
  public bool IsGenerated { get; init; }
}

public sealed record TableAnalysis
{
  public required string Name { get; init; }
  public string Schema { get; init; } = SchemaToolsDefaults.DefaultSchema;
  public string? Category { get; init; }
  public string? Description { get; init; }
  public string SourceFile { get; init; } = string.Empty;

  // Soft-delete pattern detection
  public bool HasActiveColumn { get; init; }
  public bool HasTemporalVersioning { get; init; }
  public bool HasSoftDelete { get; init; }
  public string? ActiveColumnName { get; init; }

  public string? HistoryTable { get; init; }
  public string? ValidFromColumn { get; init; }
  public string? ValidToColumn { get; init; }

  /// <summary>
  /// Soft-delete trigger mode for this table.
  /// Determines whether cascade, restrict, or ignore behaviour is used.
  /// </summary>
  public SoftDeleteMode SoftDeleteMode { get; init; } = SoftDeleteMode.Cascade;

  /// <summary>
  /// Whether reactivation cascade is enabled for this table.
  /// When true, reactivating this parent will auto-reactivate children
  /// that were soft-deleted at the same time.
  /// </summary>
  public bool ReactivationCascade { get; init; } = false;

  /// <summary>
  /// Tolerance in milliseconds for matching child soft-delete timestamps
  /// during reactivation cascade.
  /// </summary>
  public int ReactivationCascadeToleranceMs { get; init; } = SchemaToolsDefaults.ReactivationCascadeToleranceMs;

  // Keys and relationships
  public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = [];
  public IReadOnlyList<ForeignKeyRef> ForeignKeyReferences { get; init; } = [];

  /// <summary>
  /// Column-level descriptions parsed from trailing <c>@description</c> annotations.
  /// Keys are column names; values are description text.
  /// </summary>
  public IReadOnlyDictionary<string, string>? ColumnDescriptions { get; init; }

  // FK graph (computed)
  public IReadOnlyList<string> ChildTables { get; init; } = [];
  public bool IsLeafTable { get; init; }
}

public sealed record ForeignKeyRef
{
  public required string ReferencedTable { get; init; }
  public string ReferencedSchema { get; init; } = SchemaToolsDefaults.DefaultSchema;
  public IReadOnlyList<string> Columns { get; init; } = [];
  public IReadOnlyList<string> ReferencedColumns { get; init; } = [];
  public ForeignKeyAction OnDelete { get; init; } = ForeignKeyAction.NoAction;
}
