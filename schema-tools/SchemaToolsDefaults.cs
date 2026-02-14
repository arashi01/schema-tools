namespace SchemaTools;

/// <summary>
/// Centralised default values for SchemaTools configuration.
/// All default constants are defined here to ensure consistency across the codebase.
/// </summary>
public static class SchemaToolsDefaults
{
  // ===========================================================================
  // Versioning
  // ===========================================================================

  /// <summary>
  /// Current schema metadata format version.
  /// </summary>
  public const string MetadataVersion = "1.0.0";

  // ===========================================================================
  // SQL Server Configuration
  // ===========================================================================

  /// <summary>
  /// Default SQL Server version (SQL Server 2025).
  /// </summary>
  public const string SqlServerVersion = "Sql170";

  /// <summary>
  /// Default database schema.
  /// </summary>
  public const string DefaultSchema = "dbo";

  // ===========================================================================
  // Soft-Delete Column Names
  // ===========================================================================

  /// <summary>
  /// Default soft-delete flag column name.
  /// </summary>
  public const string ActiveColumn = "active";

  /// <summary>
  /// SQL literal representing active state.
  /// </summary>
  public const string ActiveValue = "1";

  /// <summary>
  /// SQL literal representing inactive/soft-deleted state.
  /// </summary>
  public const string InactiveValue = "0";

  // ===========================================================================
  // Audit Column Names
  // ===========================================================================

  /// <summary>
  /// Default created timestamp column name.
  /// </summary>
  public const string CreatedAtColumn = "created_at";

  /// <summary>
  /// Default creator audit column name.
  /// </summary>
  public const string CreatedByColumn = "created_by";

  /// <summary>
  /// Default last updater audit column name.
  /// </summary>
  public const string UpdatedByColumn = "updated_by";

  /// <summary>
  /// SQL data type for updated_by column in triggers.
  /// </summary>
  public const string UpdatedByType = "UNIQUEIDENTIFIER";

  // ===========================================================================
  // Temporal Versioning Column Names
  // ===========================================================================

  /// <summary>
  /// Default temporal period start column name.
  /// </summary>
  public const string ValidFromColumn = "valid_from";

  /// <summary>
  /// Default temporal period end column name.
  /// </summary>
  public const string ValidToColumn = "valid_to";

  // ===========================================================================
  // Purge Configuration
  // ===========================================================================

  /// <summary>
  /// Default purge stored procedure name.
  /// </summary>
  public const string PurgeProcedureName = "usp_purge_soft_deleted";

  /// <summary>
  /// Default grace period (in days) before soft-deleted records can be purged.
  /// </summary>
  public const int DefaultGracePeriodDays = 90;

  /// <summary>
  /// Default batch size for purge operations.
  /// </summary>
  public const int DefaultBatchSize = 1000;

  // ===========================================================================
  // FK Relationship Defaults
  // ===========================================================================

  /// <summary>
  /// Default FK on-delete action.
  /// </summary>
  public const string ForeignKeyOnDeleteDefault = "NoAction";

  // ===========================================================================
  // View Generation
  // ===========================================================================

  /// <summary>
  /// Default view naming pattern. {table} is replaced with the table name.
  /// </summary>
  public const string ViewNamingPattern = "vw_{table}";

  /// <summary>
  /// Default deleted view naming pattern for soft-deleted records.
  /// </summary>
  public const string DeletedViewNamingPattern = "vw_{table}_deleted";

  // ===========================================================================
  // Reactivation Cascade
  // ===========================================================================

  /// <summary>
  /// Default tolerance (in milliseconds) for matching child soft-delete timestamps
  /// to the parent during reactivation cascade.
  /// </summary>
  public const int ReactivationCascadeToleranceMs = 2000;
}
