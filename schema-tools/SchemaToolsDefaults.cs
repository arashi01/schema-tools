namespace SchemaTools;

/// <summary>
/// Centralised default values for SchemaTools configuration.
/// </summary>
public static class SchemaToolsDefaults
{
  public const string MetadataVersion = "1.0.0";
  public const string SqlServerVersion = "Sql170";
  public const string DefaultSchema = "dbo";

  public const string ActiveColumn = "record_active";
  public const string ActiveValue = "1";
  public const string InactiveValue = "0";

  public const string CreatedAtColumn = "record_created_at";
  public const string CreatedByColumn = "record_created_by";
  public const string UpdatedByColumn = "record_updated_by";
  public const string UpdatedByType = "UNIQUEIDENTIFIER";

  public const string ValidFromColumn = "record_valid_from";
  public const string ValidToColumn = "record_valid_until";

  public const string PurgeProcedureName = "usp_purge_soft_deleted";
  public const int DefaultGracePeriodDays = 90;
  public const int DefaultBatchSize = 1000;

  public const string ForeignKeyOnDeleteDefault = "NoAction";

  public const string ViewNamingPattern = "vw_{table}";
  public const string DeletedViewNamingPattern = "vw_{table}_deleted";

  public const int ReactivationCascadeToleranceMs = 2000;
}
