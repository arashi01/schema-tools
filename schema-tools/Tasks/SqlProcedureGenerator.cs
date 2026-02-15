using System.Text;
using Microsoft.Build.Framework;
using SchemaTools.Models;
using SchemaTools.Utilities;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Generates stored procedures for deferred hard-delete of soft-deleted records.
/// 
/// Design Principles:
/// - Hard delete is ALWAYS deferred (no immediate deletion)
/// - Deletes in FK-safe topological order (leaves first, then parents)
/// - Respects configurable grace period
/// - Single transaction for consistency
/// - Application controls when/how to call
/// </summary>
public class SqlProcedureGenerator : MSTask
{
  /// <summary>
  /// Path to source analysis JSON from SchemaSourceAnalyser
  /// </summary>
  [Required]
  public string AnalysisFile { get; set; } = string.Empty;

  /// <summary>
  /// Output directory for generated procedure files
  /// </summary>
  [Required]
  public string OutputDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Schema for generated procedures
  /// </summary>
  public string ProcedureSchema { get; set; } = SchemaToolsDefaults.DefaultSchema;

  /// <summary>
  /// Name of the main purge procedure
  /// </summary>
  public string PurgeProcedureName { get; set; } = SchemaToolsDefaults.PurgeProcedureName;

  /// <summary>
  /// Default grace period in days before hard delete is allowed
  /// </summary>
  public int DefaultGracePeriodDays { get; set; } = SchemaToolsDefaults.DefaultGracePeriodDays;

  /// <summary>
  /// Force regeneration even if files exist
  /// </summary>
  public bool Force { get; set; }

  // Testing support
  internal SourceAnalysisResult? TestAnalysis { get; set; }

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Stored Procedure Generator");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, string.Empty);

      SourceAnalysisResult analysis = AnalysisLoader.Load(AnalysisFile, TestAnalysis);

      // Find all tables with soft-delete
      var softDeleteTables = analysis.Tables
        .Where(t => t.HasSoftDelete)
        .ToList();

      if (softDeleteTables.Count == 0)
      {
        Log.LogMessage("No soft-delete tables found - no purge procedure needed");
        return true;
      }

      Log.LogMessage(MessageImportance.High, $"Found {softDeleteTables.Count} table(s) with soft-delete");

      // Build topological order (leaves first)
      List<TableAnalysis> deletionOrder = TopologicalSort(softDeleteTables);

      Log.LogMessage(MessageImportance.High, "Deletion order (FK-safe):");
      for (int i = 0; i < deletionOrder.Count; i++)
      {
        Log.LogMessage(MessageImportance.High, $"  {i + 1}. {deletionOrder[i].Name}");
      }
      Log.LogMessage(MessageImportance.High, string.Empty);

      Directory.CreateDirectory(OutputDirectory);

      // Generate the purge procedure
      string fileName = $"{PurgeProcedureName}.sql";
      string filePath = Path.Combine(OutputDirectory, fileName);

      if (File.Exists(filePath) && !Force)
      {
        Log.LogMessage($"Skipped {fileName}: Already exists (use Force=true)");
      }
      else
      {
        string procSql = GeneratePurgeProcedure(deletionOrder);
        File.WriteAllText(filePath, procSql, Encoding.UTF8);
        Log.LogMessage($"+ Generated: {fileName}");
      }

      // Summary
      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Summary");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, $"Procedure:  [{ProcedureSchema}].[{PurgeProcedureName}]");
      Log.LogMessage(MessageImportance.High, $"Tables:     {deletionOrder.Count}");
      Log.LogMessage(MessageImportance.High, $"Output:     {OutputDirectory}");
      Log.LogMessage(MessageImportance.High, "============================================================");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Procedure generation failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }



  /// <summary>
  /// Topological sort to get FK-safe deletion order (leaves first, parents last)
  /// </summary>
  private List<TableAnalysis> TopologicalSort(List<TableAnalysis> tables)
  {
    var result = new List<TableAnalysis>();
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var tableLookup = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    void Visit(TableAnalysis table)
    {
      if (visited.Contains(table.Name))
        return;

      if (visiting.Contains(table.Name))
      {
        Log.LogWarning($"Circular dependency detected involving {table.Name}");
        return;
      }

      visiting.Add(table.Name);

      // Visit all children first (they must be deleted before parent)
      foreach (string childName in table.ChildTables)
      {
        if (tableLookup.TryGetValue(childName, out TableAnalysis? child))
        {
          Visit(child);
        }
      }

      visiting.Remove(table.Name);
      visited.Add(table.Name);
      result.Add(table);
    }

    // Start from tables with no parents (roots) and work down
    // But we want leaves first, so we reverse the dependency direction
    foreach (TableAnalysis table in tables.OrderBy(t => t.ChildTables.Count))
    {
      Visit(table);
    }

    return result;
  }

  private string GeneratePurgeProcedure(List<TableAnalysis> deletionOrder)
  {
    var sb = new StringBuilder();

    sb.AppendLine($@"-- =============================================================================
-- Purge Soft-Deleted Records Procedure
-- Generated by: SchemaTools.SqlProcedureGenerator
-- Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
--
-- Purpose:
--   Hard-deletes soft-deleted records that have exceeded the grace period.
--   Deletes in FK-safe topological order to prevent constraint violations.
--
-- Tables processed ({deletionOrder.Count}):");

    for (int i = 0; i < deletionOrder.Count; i++)
    {
      sb.AppendLine($"--   {i + 1}. {deletionOrder[i].Name}");
    }

    sb.AppendLine($@"--
-- Parameters:
--   @grace_period_days: Number of days after soft-delete before hard-delete (default: {DefaultGracePeriodDays})
--   @batch_size: Maximum records to delete per table per execution (default: 1000, 0 = unlimited)
--   @dry_run: If 1, reports what would be deleted without actually deleting
--
-- Usage:
--   EXEC [{ProcedureSchema}].[{PurgeProcedureName}];  -- Use defaults
--   EXEC [{ProcedureSchema}].[{PurgeProcedureName}] @grace_period_days = 30;
--   EXEC [{ProcedureSchema}].[{PurgeProcedureName}] @dry_run = 1;  -- Preview only
--
-- DO NOT EDIT MANUALLY - regenerate with Force=true
-- =============================================================================

CREATE PROCEDURE [{ProcedureSchema}].[{PurgeProcedureName}]
    @grace_period_days INT = {DefaultGracePeriodDays},
    @batch_size INT = 1000,
    @dry_run BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @cutoff_date DATETIMEOFFSET(7) = DATEADD(DAY, -@grace_period_days, SYSUTCDATETIME());
    DECLARE @deleted_count INT;
    DECLARE @total_deleted INT = 0;
    DECLARE @start_time DATETIME2 = SYSUTCDATETIME();

    -- Results table for reporting
    CREATE TABLE #purge_results (
        table_name NVARCHAR(128),
        records_deleted INT,
        execution_order INT
    );

    IF @dry_run = 1
    BEGIN
        PRINT 'DRY RUN MODE - No records will be deleted';
        PRINT 'Cutoff date: ' + CONVERT(VARCHAR(30), @cutoff_date, 120);
        PRINT '';
    END

    BEGIN TRY
        IF @dry_run = 0
            BEGIN TRANSACTION;
");

    // Generate DELETE statements for each table in topological order
    for (int i = 0; i < deletionOrder.Count; i++)
    {
      TableAnalysis table = deletionOrder[i];
      string schema = table.Schema ?? ProcedureSchema;
      string activeColumn = table.ActiveColumnName ?? "active";
      string validToColumn = table.ValidToColumn ?? "valid_to";

      // Build PK column list for JOIN
      List<string> pkColumns = table.PrimaryKeyColumns.Count > 0
        ? table.PrimaryKeyColumns
        : new List<string> { "id" };
      string pkJoinConditions = string.Join(" AND ", pkColumns.Select(pk => $"t.{pk} = h.{pk}"));

      // Check if we have history table info for proper grace period checking
      bool hasHistoryTable = !string.IsNullOrEmpty(table.HistoryTable) && table.HasTemporalVersioning;

      sb.AppendLine($@"
        -- {i + 1}. [{schema}].[{table.Name}]");

      if (hasHistoryTable)
      {
        // Use temporal history to properly check grace period
        // Find records that were soft-deleted before the cutoff date
        sb.AppendLine($@"        IF @dry_run = 1
        BEGIN
            -- Count records eligible for purge (soft-deleted before cutoff)
            SELECT @deleted_count = COUNT(DISTINCT t.{pkColumns[0]})
            FROM [{schema}].[{table.Name}] t
            WHERE t.{activeColumn} = 0
            AND EXISTS (
                SELECT 1 FROM {table.HistoryTable} h
                WHERE {pkJoinConditions}
                AND h.{activeColumn} = 1                 -- Was previously active
                AND h.{validToColumn} <= @cutoff_date    -- Became inactive before cutoff
            );
        END
        ELSE
        BEGIN
            DELETE{(i > 0 ? " TOP (@batch_size)" : "")} t
            FROM [{schema}].[{table.Name}] t
            WHERE t.{activeColumn} = 0
            AND EXISTS (
                SELECT 1 FROM {table.HistoryTable} h
                WHERE {pkJoinConditions}
                AND h.{activeColumn} = 1                 -- Was previously active
                AND h.{validToColumn} <= @cutoff_date    -- Became inactive before cutoff
            );
            SET @deleted_count = @@ROWCOUNT;
        END");
      }
      else
      {
        // Fallback for tables without history info - uses current valid_from as a proxy
        // This is less accurate but safe (won't delete recent records)
        sb.AppendLine($@"        -- Warning: No history table detected - using {validToColumn} approximation
        IF @dry_run = 1
        BEGIN
            SELECT @deleted_count = COUNT(*)
            FROM [{schema}].[{table.Name}]
            WHERE {activeColumn} = 0
            AND {validToColumn} <= @cutoff_date;  -- Approximation only
        END
        ELSE
        BEGIN
            DELETE{(i > 0 ? " TOP (@batch_size)" : "")} FROM [{schema}].[{table.Name}]
            WHERE {activeColumn} = 0
            AND {validToColumn} <= @cutoff_date;  -- Approximation only
            SET @deleted_count = @@ROWCOUNT;
        END");
      }

      sb.AppendLine($@"
        INSERT INTO #purge_results (table_name, records_deleted, execution_order)
        VALUES ('{table.Name}', @deleted_count, {i + 1});

        SET @total_deleted = @total_deleted + @deleted_count;
");
    }

    sb.AppendLine($@"
        IF @dry_run = 0
            COMMIT TRANSACTION;

        -- Report results
        SELECT 
            table_name AS [Table],
            records_deleted AS [Records Deleted],
            execution_order AS [Order]
        FROM #purge_results
        WHERE records_deleted > 0
        ORDER BY execution_order;

        SELECT 
            @total_deleted AS [Total Records Deleted],
            DATEDIFF(MILLISECOND, @start_time, SYSUTCDATETIME()) AS [Duration (ms)],
            @grace_period_days AS [Grace Period (days)],
            @cutoff_date AS [Cutoff Date],
            CASE WHEN @dry_run = 1 THEN 'DRY RUN' ELSE 'EXECUTED' END AS [Mode];

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 AND @dry_run = 0
            ROLLBACK TRANSACTION;

        DECLARE @error_msg NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @error_severity INT = ERROR_SEVERITY();
        DECLARE @error_state INT = ERROR_STATE();

        RAISERROR(@error_msg, @error_severity, @error_state);
    END CATCH

    DROP TABLE IF EXISTS #purge_results;
END;
GO
");

    return sb.ToString();
  }
}
