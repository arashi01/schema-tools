using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using SchemaTools.Models;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Generates soft-delete triggers based on table configuration.
/// 
/// Trigger Types by Mode:
/// - Cascade: Parent triggers that propagate active=0 to children
/// - Restrict: Parent triggers that block soft-delete if active children exist
/// - Ignore: No triggers generated for this table
/// 
/// Additionally generates:
/// - Reactivation Guard triggers for child tables (prevent reactivating while parent inactive)
/// 
/// Design Principles:
/// - Parent tables (with FK children): Generate trigger that propagates active=0 to children
/// - Leaf tables (no children): No trigger needed (nothing to cascade)
/// - NO hard-delete triggers - hard delete is deferred and handled by SqlProcedureGenerator
/// 
/// This ensures:
/// - Semantic correctness (deleting parent cascades to children)
/// - FK integrity (children soft-deleted before parent could be hard-deleted)
/// - Recoverability (no immediate hard delete, grace period preserved)
/// </summary>
public class SqlTriggerGenerator : MSTask
{
  /// <summary>
  /// Path to source analysis JSON from SchemaSourceAnalyser
  /// </summary>
  [Required]
  public string AnalysisFile { get; set; } = string.Empty;

  /// <summary>
  /// Output directory for generated trigger files
  /// </summary>
  [Required]
  public string OutputDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Force regeneration even if files exist
  /// </summary>
  public bool Force { get; set; }

  /// <summary>
  /// Schema name for generated triggers (default: from analysis)
  /// </summary>
  public string DefaultSchema { get; set; } = "dbo";

  // Testing support
  internal SourceAnalysisResult? TestAnalysis { get; set; }

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Soft-Delete Trigger Generator");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, string.Empty);

      SourceAnalysisResult analysis = LoadAnalysis();

      // Build lookup for table details
      var tableLookup = analysis.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

      // Build lookup for explicit triggers (not in _generated directory)
      // Explicit triggers always take precedence over generated ones
      var explicitTriggers = analysis.ExistingTriggers
        .Where(t => !t.IsGenerated)
        .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

      Directory.CreateDirectory(OutputDirectory);

      int cascadeGenerated = 0;
      int restrictGenerated = 0;
      int guardGenerated = 0;
      int reactivationCascadeGenerated = 0;
      int skippedIgnore = 0;
      int skippedExplicit = 0;
      int skippedExists = 0;

      // ============================================================================
      // PHASE 1: Parent Table Triggers (Cascade or Restrict mode)
      // ============================================================================

      // Parent tables with soft-delete and children, excluding Ignore mode
      var parentTables = analysis.Tables
        .Where(t => t.HasSoftDelete && t.ChildTables.Count > 0 && t.SoftDeleteMode != SoftDeleteMode.Ignore)
        .ToList();

      // Count ignored tables for reporting
      skippedIgnore = analysis.Tables
        .Count(t => t.HasSoftDelete && t.ChildTables.Count > 0 && t.SoftDeleteMode == SoftDeleteMode.Ignore);

      if (parentTables.Count > 0)
      {
        int cascadeCount = parentTables.Count(t => t.SoftDeleteMode == SoftDeleteMode.Cascade);
        int restrictCount = parentTables.Count(t => t.SoftDeleteMode == SoftDeleteMode.Restrict);
        Log.LogMessage(MessageImportance.High, $"Phase 1: Parent table triggers ({cascadeCount} cascade, {restrictCount} restrict)");

        foreach (TableAnalysis parent in parentTables)
        {
          // Determine trigger name based on mode
          string triggerName = parent.SoftDeleteMode == SoftDeleteMode.Restrict
            ? $"trg_{parent.Name}_restrict_soft_delete"
            : $"trg_{parent.Name}_cascade_soft_delete";
          string fileName = $"{triggerName}.sql";
          string filePath = Path.Combine(OutputDirectory, fileName);

          // EXPLICIT WINS: Check if user has defined this trigger anywhere outside _generated
          if (explicitTriggers.TryGetValue(triggerName, out ExistingTrigger? explicitTrigger))
          {
            Log.LogMessage(MessageImportance.High,
              $"  - Skipped [{triggerName}]: Explicit definition at {explicitTrigger.SourceFile}");
            skippedExplicit++;
            continue;
          }

          // Check if exists and Force not set
          if (File.Exists(filePath) && !Force)
          {
            Log.LogMessage(MessageImportance.Low, $"  Skipped {parent.Name}: Already exists");
            skippedExists++;
            continue;
          }

          // Generate trigger based on mode
          string triggerSql;
          if (parent.SoftDeleteMode == SoftDeleteMode.Restrict)
          {
            triggerSql = GenerateRestrictTrigger(parent, tableLookup, analysis.Columns);
            Log.LogMessage($"  + {fileName} (RESTRICT: blocks if children active)");
            restrictGenerated++;
          }
          else
          {
            triggerSql = GenerateCascadeTrigger(parent, tableLookup, analysis.Columns);
            Log.LogMessage($"  + {fileName} (CASCADE -> {parent.ChildTables.Count} children)");
            cascadeGenerated++;
          }
          File.WriteAllText(filePath, triggerSql, Encoding.UTF8);
        }
      }

      // ============================================================================
      // PHASE 2: Reactivation Guard Triggers (for child tables with soft-delete parents)
      // ============================================================================

      // Skip Phase 2 if generateReactivationGuards is disabled
      if (!analysis.Features.GenerateReactivationGuards)
      {
        Log.LogMessage(MessageImportance.High, string.Empty);
        Log.LogMessage(MessageImportance.High, "Phase 2: Skipped (generateReactivationGuards = false)");
      }
      else
      {
        // Child tables that have FK references to soft-delete parent tables (excluding Ignore mode)
        var childTables = analysis.Tables
          .Where(t => t.HasSoftDelete && t.ForeignKeyReferences.Count > 0 && t.SoftDeleteMode != SoftDeleteMode.Ignore)
          .Where(t => t.ForeignKeyReferences.Any(fk =>
            tableLookup.TryGetValue(fk.ReferencedTable, out TableAnalysis? parent) && parent.HasSoftDelete))
          .ToList();

        if (childTables.Count > 0)
        {
          Log.LogMessage(MessageImportance.High, string.Empty);
          Log.LogMessage(MessageImportance.High, $"Phase 2: REACTIVATION GUARD triggers for {childTables.Count} child table(s)");

          foreach (TableAnalysis child in childTables)
          {
            string triggerName = $"trg_{child.Name}_reactivation_guard";
            string fileName = $"{triggerName}.sql";
            string filePath = Path.Combine(OutputDirectory, fileName);

            // EXPLICIT WINS
            if (explicitTriggers.TryGetValue(triggerName, out ExistingTrigger? explicitTrigger))
            {
              Log.LogMessage(MessageImportance.High,
                $"  - Skipped [{triggerName}]: Explicit definition at {explicitTrigger.SourceFile}");
              skippedExplicit++;
              continue;
            }

            if (File.Exists(filePath) && !Force)
            {
              Log.LogMessage(MessageImportance.Low, $"  Skipped {child.Name}: Already exists");
              skippedExists++;
              continue;
            }

            // Generate reactivation guard trigger
            string triggerSql = GenerateReactivationGuardTrigger(child, tableLookup, analysis.Columns);
            File.WriteAllText(filePath, triggerSql, Encoding.UTF8);

            int parentCount = child.ForeignKeyReferences.Count(fk =>
              tableLookup.TryGetValue(fk.ReferencedTable, out TableAnalysis? p) && p.HasSoftDelete);
            Log.LogMessage($"  + {fileName} (checks {parentCount} parent(s))");
            guardGenerated++;
          }
        }
      }

      // ============================================================================
      // PHASE 3: Reactivation Cascade Triggers (for parent tables with ReactivationCascade enabled)
      // ============================================================================

      // Parent tables with ReactivationCascade enabled and children
      var reactivationCascadeTables = analysis.Tables
        .Where(t => t.HasSoftDelete && t.ReactivationCascade && t.ChildTables.Count > 0)
        .ToList();

      if (reactivationCascadeTables.Count > 0)
      {
        Log.LogMessage(MessageImportance.High, string.Empty);
        Log.LogMessage(MessageImportance.High, $"Phase 3: REACTIVATION CASCADE triggers for {reactivationCascadeTables.Count} parent table(s)");

        foreach (TableAnalysis parent in reactivationCascadeTables)
        {
          string triggerName = $"trg_{parent.Name}_cascade_reactivation";
          string fileName = $"{triggerName}.sql";
          string filePath = Path.Combine(OutputDirectory, fileName);

          // EXPLICIT WINS
          if (explicitTriggers.TryGetValue(triggerName, out ExistingTrigger? explicitTrigger))
          {
            Log.LogMessage(MessageImportance.High,
              $"  - Skipped [{triggerName}]: Explicit definition at {explicitTrigger.SourceFile}");
            skippedExplicit++;
            continue;
          }

          if (File.Exists(filePath) && !Force)
          {
            Log.LogMessage(MessageImportance.Low, $"  Skipped {parent.Name}: Already exists");
            skippedExists++;
            continue;
          }

          // Generate reactivation cascade trigger
          string triggerSql = GenerateReactivationCascadeTrigger(parent, tableLookup, analysis.Columns);
          File.WriteAllText(filePath, triggerSql, Encoding.UTF8);

          Log.LogMessage($"  + {fileName} (cascades to {parent.ChildTables.Count} children)");
          reactivationCascadeGenerated++;
        }
      }

      // Summary
      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Summary");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, $"Cascade triggers:           {cascadeGenerated}");
      Log.LogMessage(MessageImportance.High, $"Restrict triggers:          {restrictGenerated}");
      Log.LogMessage(MessageImportance.High, $"Reactivation guards:        {guardGenerated}");
      Log.LogMessage(MessageImportance.High, $"Reactivation cascades:      {reactivationCascadeGenerated}");
      if (skippedIgnore > 0)
        Log.LogMessage(MessageImportance.High, $"Ignored (by config):        {skippedIgnore}");
      if (skippedExplicit > 0)
        Log.LogMessage(MessageImportance.High, $"Explicit overrides:         {skippedExplicit}");
      if (skippedExists > 0)
        Log.LogMessage(MessageImportance.High, $"Unchanged:                  {skippedExists}");
      Log.LogMessage(MessageImportance.High, $"Output dir:                 {OutputDirectory}");
      Log.LogMessage(MessageImportance.High, "============================================================");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Trigger generation failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private SourceAnalysisResult LoadAnalysis()
  {
    if (TestAnalysis != null)
    {
      Log.LogMessage("Using injected test analysis");
      return TestAnalysis;
    }

    if (!File.Exists(AnalysisFile))
    {
      throw new FileNotFoundException($"Analysis file not found: {AnalysisFile}");
    }

    string json = File.ReadAllText(AnalysisFile);
    SourceAnalysisResult? analysis = JsonSerializer.Deserialize<SourceAnalysisResult>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (analysis == null || analysis.Tables == null)
    {
      throw new InvalidOperationException("Failed to deserialize analysis file");
    }

    return analysis;
  }

  #region Helper Methods for Multi-Column Key Support

  /// <summary>
  /// Builds a JOIN condition for PK columns (same column names on both sides).
  /// Example: "i.col1 = d.col1 AND i.col2 = d.col2"
  /// </summary>
  private static string BuildPkJoinCondition(List<string> pkColumns, string leftAlias, string rightAlias)
  {
    if (pkColumns.Count == 0)
      return $"{leftAlias}.id = {rightAlias}.id"; // fallback

    if (pkColumns.Count == 1)
      return $"{leftAlias}.{pkColumns[0]} = {rightAlias}.{pkColumns[0]}";

    return string.Join(" AND ", pkColumns.Select(c => $"{leftAlias}.{c} = {rightAlias}.{c}"));
  }

  /// <summary>
  /// Builds a JOIN condition for FK relationships (different column names).
  /// Example: "child.parent_id = parent.id AND child.tenant_id = parent.tenant_id"
  /// </summary>
  private static string BuildFkJoinCondition(
    List<string> fkColumns,
    List<string> referencedColumns,
    string childAlias,
    string parentAlias)
  {
    if (fkColumns.Count == 0 || referencedColumns.Count == 0)
      return $"{childAlias}.id = {parentAlias}.id"; // fallback

    if (fkColumns.Count == 1)
      return $"{childAlias}.{fkColumns[0]} = {parentAlias}.{referencedColumns[0]}";

    return string.Join(" AND ",
      fkColumns.Zip(referencedColumns, (fk, rf) => $"{childAlias}.{fk} = {parentAlias}.{rf}"));
  }

  #endregion

  private string GenerateCascadeTrigger(
    TableAnalysis parent,
    Dictionary<string, TableAnalysis> tableLookup,
    ColumnConfig columns)
  {
    string schema = parent.Schema ?? DefaultSchema;
    string activeColumn = parent.ActiveColumnName ?? "active";
    List<string> primaryKeyColumns = parent.PrimaryKeyColumns.Count > 0
      ? parent.PrimaryKeyColumns
      : new List<string> { "id" };

    var sb = new StringBuilder();

    // Header
    sb.AppendLine($@"-- =================================================================================
-- Cascade Soft-Delete Trigger for [{schema}].[{parent.Name}]
-- Generated by: SchemaTools.SqlTriggerGenerator
-- Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
--
-- Purpose:
--   When this parent record is soft-deleted (active = 0), automatically cascade
--   soft-delete to all child records to maintain referential consistency.
--   Hard delete is handled separately via stored procedure after grace period.
--
-- Cascades to {parent.ChildTables.Count} child table(s):");

    foreach (string childName in parent.ChildTables)
    {
      sb.AppendLine($"--   - {childName}");
    }

    sb.AppendLine(@"--
-- DO NOT EDIT MANUALLY - regenerate with Force=true
-- =================================================================================
");

    sb.AppendLine($"CREATE TRIGGER [{schema}].[trg_{parent.Name}_cascade_soft_delete]");
    sb.AppendLine($"ON [{schema}].[{parent.Name}]");
    sb.AppendLine("AFTER UPDATE");
    sb.AppendLine("AS");
    sb.AppendLine("BEGIN");
    sb.AppendLine("    SET NOCOUNT ON;");
    sb.AppendLine();
    sb.AppendLine($"    -- Only proceed if '{activeColumn}' column was updated");
    sb.AppendLine($"    IF NOT UPDATE({activeColumn})");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();
    sb.AppendLine("    -- Check if any rows were actually soft-deleted (active: 1 -> 0)");
    sb.AppendLine("    IF NOT EXISTS (");
    sb.AppendLine("        SELECT 1");
    sb.AppendLine("        FROM inserted i");
    sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
    sb.AppendLine($"        WHERE i.{activeColumn} = {columns.InactiveValue} AND d.{activeColumn} = {columns.ActiveValue}");
    sb.AppendLine("    )");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();
    sb.AppendLine("    -- Capture the user performing the soft-delete for audit trail");
    sb.AppendLine($"    DECLARE @updated_by {columns.UpdatedByType};");
    sb.AppendLine($"    SELECT TOP 1 @updated_by = {columns.UpdatedBy} FROM inserted;");
    sb.AppendLine();

    // Generate cascade UPDATE for each child table
    foreach (string childName in parent.ChildTables)
    {
      if (!tableLookup.TryGetValue(childName, out TableAnalysis? child))
      {
        sb.AppendLine($"    -- Skipping {childName}: Not found in analysis");
        continue;
      }

      if (!child.HasSoftDelete)
      {
        sb.AppendLine($"    -- Skipping {childName}: Does not have soft-delete enabled");
        continue;
      }

      // Find the FK from child to this parent
      ForeignKeyRef? fkToParent = child.ForeignKeyReferences
        .FirstOrDefault(fk => string.Equals(fk.ReferencedTable, parent.Name, StringComparison.OrdinalIgnoreCase));

      if (fkToParent == null)
      {
        sb.AppendLine($"    -- Skipping {childName}: FK relationship not found");
        continue;
      }

      string childSchema = child.Schema ?? DefaultSchema;
      string childActiveColumn = child.ActiveColumnName ?? "active";

      // Get FK columns (supports composite FKs)
      List<string> fkColumns = fkToParent.Columns.Count > 0
        ? fkToParent.Columns
        : new List<string> { "id" };
      List<string> referencedColumns = fkToParent.ReferencedColumns.Count > 0
        ? fkToParent.ReferencedColumns
        : primaryKeyColumns;

      sb.AppendLine($"    -- Cascade to [{childSchema}].[{childName}]");

      if (fkColumns.Count == 1)
      {
        // Single-column FK: use simple IN clause
        sb.AppendLine($"    UPDATE [{childSchema}].[{childName}]");
        sb.AppendLine("    SET");
        sb.AppendLine($"        {childActiveColumn} = {columns.InactiveValue},");
        sb.AppendLine($"        {columns.UpdatedBy} = @updated_by");
        sb.AppendLine($"    WHERE {fkColumns[0]} IN (");
        sb.AppendLine($"        SELECT i.{referencedColumns[0]}");
        sb.AppendLine("        FROM inserted i");
        sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
        sb.AppendLine($"        WHERE i.{activeColumn} = {columns.InactiveValue} AND d.{activeColumn} = {columns.ActiveValue}");
        sb.AppendLine("    )");
        sb.AppendLine($"    AND {childActiveColumn} = {columns.ActiveValue};  -- Only cascade to active children");
      }
      else
      {
        // Multi-column FK: use EXISTS with correlated subquery
        sb.AppendLine($"    UPDATE c");
        sb.AppendLine("    SET");
        sb.AppendLine($"        c.{childActiveColumn} = {columns.InactiveValue},");
        sb.AppendLine($"        c.{columns.UpdatedBy} = @updated_by");
        sb.AppendLine($"    FROM [{childSchema}].[{childName}] c");
        sb.AppendLine("    WHERE EXISTS (");
        sb.AppendLine("        SELECT 1");
        sb.AppendLine("        FROM inserted i");
        sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
        sb.AppendLine($"        WHERE i.{activeColumn} = {columns.InactiveValue} AND d.{activeColumn} = {columns.ActiveValue}");
        sb.AppendLine($"        AND {BuildFkJoinCondition(fkColumns, referencedColumns, "c", "i")}");
        sb.AppendLine("    )");
        sb.AppendLine($"    AND c.{childActiveColumn} = {columns.ActiveValue};  -- Only cascade to active children");
      }
      sb.AppendLine();
    }

    sb.AppendLine("END;");
    sb.AppendLine("GO");

    return sb.ToString();
  }

  /// <summary>
  /// Generates a reactivation guard trigger for a child table.
  /// Prevents reactivating a record (active 0->1) if any referenced parent is inactive.
  /// </summary>
  private string GenerateReactivationGuardTrigger(
    TableAnalysis child,
    Dictionary<string, TableAnalysis> tableLookup,
    ColumnConfig columns)
  {
    string schema = child.Schema ?? DefaultSchema;
    string activeColumn = child.ActiveColumnName ?? "active";
    List<string> primaryKeyColumns = child.PrimaryKeyColumns.Count > 0
      ? child.PrimaryKeyColumns
      : new List<string> { "id" };

    // Get all soft-delete parent references
    var softDeleteParents = child.ForeignKeyReferences
      .Where(fk => tableLookup.TryGetValue(fk.ReferencedTable, out TableAnalysis? p) && p.HasSoftDelete)
      .Select(fk => (FK: fk, Parent: tableLookup[fk.ReferencedTable]))
      .ToList();

    var sb = new StringBuilder();

    // Header
    sb.AppendLine($@"-- =================================================================================
-- Reactivation Guard Trigger for [{schema}].[{child.Name}]
-- Generated by: SchemaTools.SqlTriggerGenerator
-- Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
--
-- Purpose:
--   Prevents reactivating a child record (active: 0 -> 1) if any of its
--   referenced parent records are still inactive. This maintains referential
--   consistency in soft-delete hierarchies.
--
-- Guards against inactive parents:");

    foreach ((ForeignKeyRef fk, TableAnalysis parent) in softDeleteParents)
    {
      sb.AppendLine($"--   - {parent.Name} via {string.Join(", ", fk.Columns)}");
    }

    sb.AppendLine(@"--
-- DO NOT EDIT MANUALLY - regenerate with Force=true
-- =================================================================================
");

    sb.AppendLine($"CREATE TRIGGER [{schema}].[trg_{child.Name}_reactivation_guard]");
    sb.AppendLine($"ON [{schema}].[{child.Name}]");
    sb.AppendLine("AFTER UPDATE");
    sb.AppendLine("AS");
    sb.AppendLine("BEGIN");
    sb.AppendLine("    SET NOCOUNT ON;");
    sb.AppendLine();
    sb.AppendLine($"    -- Only proceed if '{activeColumn}' column was updated");
    sb.AppendLine($"    IF NOT UPDATE({activeColumn})");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();
    sb.AppendLine("    -- Check if any rows are being reactivated (active: 0 -> 1)");
    sb.AppendLine("    IF NOT EXISTS (");
    sb.AppendLine("        SELECT 1");
    sb.AppendLine("        FROM inserted i");
    sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
    sb.AppendLine($"        WHERE i.{activeColumn} = {columns.ActiveValue} AND d.{activeColumn} = {columns.InactiveValue}");
    sb.AppendLine("    )");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();

    // Check each parent FK
    foreach ((ForeignKeyRef fk, TableAnalysis parent) in softDeleteParents)
    {
      string parentSchema = parent.Schema ?? DefaultSchema;
      string parentActiveColumn = parent.ActiveColumnName ?? "active";

      List<string> fkColumns = fk.Columns.Count > 0 ? fk.Columns : new List<string> { "id" };
      List<string> refColumns = fk.ReferencedColumns.Count > 0
        ? fk.ReferencedColumns
        : parent.PrimaryKeyColumns.Count > 0 ? parent.PrimaryKeyColumns : new List<string> { "id" };

      sb.AppendLine($"    -- Check parent: [{parentSchema}].[{parent.Name}]");
      sb.AppendLine("    IF EXISTS (");
      sb.AppendLine("        SELECT 1");
      sb.AppendLine("        FROM inserted i");
      sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
      sb.AppendLine($"        JOIN [{parentSchema}].[{parent.Name}] p ON {BuildFkJoinCondition(fkColumns, refColumns, "i", "p")}");
      sb.AppendLine($"        WHERE i.{activeColumn} = {columns.ActiveValue} AND d.{activeColumn} = {columns.InactiveValue}");
      sb.AppendLine($"        AND p.{parentActiveColumn} = {columns.InactiveValue}");
      sb.AppendLine("    )");
      sb.AppendLine("    BEGIN");
      sb.AppendLine($"        RAISERROR('Cannot reactivate [{child.Name}]: Parent [{parent.Name}] is inactive. Reactivate parent first.', 16, 1);");
      sb.AppendLine("        ROLLBACK TRANSACTION;");
      sb.AppendLine("        RETURN;");
      sb.AppendLine("    END");
      sb.AppendLine();
    }

    sb.AppendLine("END;");
    sb.AppendLine("GO");

    return sb.ToString();
  }

  /// <summary>
  /// Generates a restrict trigger for a parent table.
  /// Blocks soft-delete if any active children exist - forces explicit child handling first.
  /// </summary>
  private string GenerateRestrictTrigger(
    TableAnalysis parent,
    Dictionary<string, TableAnalysis> tableLookup,
    ColumnConfig columns)
  {
    string schema = parent.Schema ?? DefaultSchema;
    string activeColumn = parent.ActiveColumnName ?? "active";
    List<string> primaryKeyColumns = parent.PrimaryKeyColumns.Count > 0
      ? parent.PrimaryKeyColumns
      : new List<string> { "id" };

    var sb = new StringBuilder();

    // Header
    sb.AppendLine($@"-- =================================================================================
-- Restrict Soft-Delete Trigger for [{schema}].[{parent.Name}]
-- Generated by: SchemaTools.SqlTriggerGenerator
-- Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
--
-- Purpose:
--   Blocks soft-delete of this parent record if any active children exist.
--   Children must be explicitly soft-deleted or removed before the parent.
--   This mode requires explicit handling rather than automatic cascade.
--
-- Checked child table(s):");

    foreach (string childName in parent.ChildTables)
    {
      sb.AppendLine($"--   - {childName}");
    }

    sb.AppendLine(@"--
-- DO NOT EDIT MANUALLY - regenerate with Force=true
-- =================================================================================
");

    sb.AppendLine($"CREATE TRIGGER [{schema}].[trg_{parent.Name}_restrict_soft_delete]");
    sb.AppendLine($"ON [{schema}].[{parent.Name}]");
    sb.AppendLine("AFTER UPDATE");
    sb.AppendLine("AS");
    sb.AppendLine("BEGIN");
    sb.AppendLine("    SET NOCOUNT ON;");
    sb.AppendLine();
    sb.AppendLine($"    -- Only proceed if '{activeColumn}' column was updated");
    sb.AppendLine($"    IF NOT UPDATE({activeColumn})");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();
    sb.AppendLine("    -- Check if any rows were actually soft-deleted (active: 1 -> 0)");
    sb.AppendLine("    IF NOT EXISTS (");
    sb.AppendLine("        SELECT 1");
    sb.AppendLine("        FROM inserted i");
    sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
    sb.AppendLine($"        WHERE i.{activeColumn} = {columns.InactiveValue} AND d.{activeColumn} = {columns.ActiveValue}");
    sb.AppendLine("    )");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();

    // Generate check for each child table
    foreach (string childName in parent.ChildTables)
    {
      if (!tableLookup.TryGetValue(childName, out TableAnalysis? child))
      {
        sb.AppendLine($"    -- Skipping {childName}: Not found in analysis");
        continue;
      }

      if (!child.HasSoftDelete)
      {
        sb.AppendLine($"    -- Skipping {childName}: Does not have soft-delete enabled");
        continue;
      }

      // Find the FK from child to this parent
      ForeignKeyRef? fkToParent = child.ForeignKeyReferences
        .FirstOrDefault(fk => string.Equals(fk.ReferencedTable, parent.Name, StringComparison.OrdinalIgnoreCase));

      if (fkToParent == null)
      {
        sb.AppendLine($"    -- Skipping {childName}: FK relationship not found");
        continue;
      }

      string childSchema = child.Schema ?? DefaultSchema;
      string childActiveColumn = child.ActiveColumnName ?? "active";

      // Get FK columns (supports composite FKs)
      List<string> fkColumns = fkToParent.Columns.Count > 0
        ? fkToParent.Columns
        : new List<string> { "id" };
      List<string> referencedColumns = fkToParent.ReferencedColumns.Count > 0
        ? fkToParent.ReferencedColumns
        : primaryKeyColumns;

      sb.AppendLine($"    -- Check for active children in [{childSchema}].[{childName}]");
      sb.AppendLine("    IF EXISTS (");
      sb.AppendLine("        SELECT 1");
      sb.AppendLine($"        FROM [{childSchema}].[{childName}] c");
      sb.AppendLine("        JOIN inserted i ON " + BuildFkJoinCondition(fkColumns, referencedColumns, "c", "i"));
      sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
      sb.AppendLine($"        WHERE i.{activeColumn} = {columns.InactiveValue} AND d.{activeColumn} = {columns.ActiveValue}");
      sb.AppendLine($"        AND c.{childActiveColumn} = {columns.ActiveValue}");
      sb.AppendLine("    )");
      sb.AppendLine("    BEGIN");
      sb.AppendLine($"        RAISERROR('Cannot soft-delete [{parent.Name}]: Active children exist in [{childName}]. Delete children first.', 16, 1);");
      sb.AppendLine("        ROLLBACK TRANSACTION;");
      sb.AppendLine("        RETURN;");
      sb.AppendLine("    END");
      sb.AppendLine();
    }

    sb.AppendLine("END;");
    sb.AppendLine("GO");

    return sb.ToString();
  }

  /// <summary>
  /// Generates a reactivation cascade trigger for a parent table.
  /// When the parent is reactivated (active 0->1), auto-reactivate children
  /// that were soft-deleted at the same time (within configurable tolerance based on valid_to).
  /// </summary>
  private string GenerateReactivationCascadeTrigger(
    TableAnalysis parent,
    Dictionary<string, TableAnalysis> tableLookup,
    ColumnConfig columns)
  {
    string schema = parent.Schema ?? DefaultSchema;
    string activeColumn = parent.ActiveColumnName ?? "active";
    string validToColumn = parent.ValidToColumn ?? columns.ValidTo;
    int toleranceMs = parent.ReactivationCascadeToleranceMs;
    List<string> primaryKeyColumns = parent.PrimaryKeyColumns.Count > 0
      ? parent.PrimaryKeyColumns
      : new List<string> { "id" };

    var sb = new StringBuilder();

    // Header
    sb.AppendLine($@"-- =================================================================================
-- Reactivation Cascade Trigger for [{schema}].[{parent.Name}]
-- Generated by: SchemaTools.SqlTriggerGenerator
-- Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
--
-- Purpose:
--   When this parent record is reactivated (active: 0 -> 1), automatically
--   reactivate child records that were soft-deleted at the same time.
--   Uses timestamp matching (valid_to within {toleranceMs}ms) to identify children
--   that were cascade-deleted together with this parent.
--
-- Cascades reactivation to {parent.ChildTables.Count} child table(s):");

    foreach (string childName in parent.ChildTables)
    {
      sb.AppendLine($"--   - {childName}");
    }

    sb.AppendLine(@"--
-- DO NOT EDIT MANUALLY - regenerate with Force=true
-- =================================================================================
");

    sb.AppendLine($"CREATE TRIGGER [{schema}].[trg_{parent.Name}_cascade_reactivation]");
    sb.AppendLine($"ON [{schema}].[{parent.Name}]");
    sb.AppendLine("AFTER UPDATE");
    sb.AppendLine("AS");
    sb.AppendLine("BEGIN");
    sb.AppendLine("    SET NOCOUNT ON;");
    sb.AppendLine();
    sb.AppendLine($"    -- Only proceed if '{activeColumn}' column was updated");
    sb.AppendLine($"    IF NOT UPDATE({activeColumn})");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();
    sb.AppendLine("    -- Check if any rows were actually reactivated (active: 0 -> 1)");
    sb.AppendLine("    IF NOT EXISTS (");
    sb.AppendLine("        SELECT 1");
    sb.AppendLine("        FROM inserted i");
    sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
    sb.AppendLine($"        WHERE i.{activeColumn} = {columns.ActiveValue} AND d.{activeColumn} = {columns.InactiveValue}");
    sb.AppendLine("    )");
    sb.AppendLine("        RETURN;");
    sb.AppendLine();
    sb.AppendLine("    -- Capture the user performing the reactivation for audit trail");
    sb.AppendLine($"    DECLARE @updated_by {columns.UpdatedByType};");
    sb.AppendLine($"    SELECT TOP 1 @updated_by = {columns.UpdatedBy} FROM inserted;");
    sb.AppendLine();

    // Generate cascade reactivation UPDATE for each child table
    foreach (string childName in parent.ChildTables)
    {
      if (!tableLookup.TryGetValue(childName, out TableAnalysis? child))
      {
        sb.AppendLine($"    -- Skipping {childName}: Not found in analysis");
        continue;
      }

      if (!child.HasSoftDelete)
      {
        sb.AppendLine($"    -- Skipping {childName}: Does not have soft-delete enabled");
        continue;
      }

      // Find the FK from child to this parent
      ForeignKeyRef? fkToParent = child.ForeignKeyReferences
        .FirstOrDefault(fk => string.Equals(fk.ReferencedTable, parent.Name, StringComparison.OrdinalIgnoreCase));

      if (fkToParent == null)
      {
        sb.AppendLine($"    -- Skipping {childName}: FK relationship not found");
        continue;
      }

      string childSchema = child.Schema ?? DefaultSchema;
      string childActiveColumn = child.ActiveColumnName ?? "active";
      string childValidToColumn = child.ValidToColumn ?? columns.ValidTo;

      // Get FK columns (supports composite FKs)
      List<string> fkColumns = fkToParent.Columns.Count > 0
        ? fkToParent.Columns
        : new List<string> { "id" };
      List<string> referencedColumns = fkToParent.ReferencedColumns.Count > 0
        ? fkToParent.ReferencedColumns
        : primaryKeyColumns;

      sb.AppendLine($"    -- Cascade reactivation to [{childSchema}].[{childName}]");
      sb.AppendLine($"    -- Only reactivate children whose valid_to is within {toleranceMs}ms of parent's valid_to");
      sb.AppendLine($"    -- (i.e., children that were soft-deleted at the same time as this parent)");

      if (fkColumns.Count == 1)
      {
        // Single-column FK: use EXISTS with correlated subquery
        sb.AppendLine($"    UPDATE c");
        sb.AppendLine("    SET");
        sb.AppendLine($"        c.{childActiveColumn} = {columns.ActiveValue},");
        sb.AppendLine($"        c.{columns.UpdatedBy} = @updated_by");
        sb.AppendLine($"    FROM [{childSchema}].[{childName}] c");
        sb.AppendLine("    WHERE EXISTS (");
        sb.AppendLine("        SELECT 1");
        sb.AppendLine("        FROM inserted i");
        sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
        sb.AppendLine($"        WHERE i.{activeColumn} = {columns.ActiveValue} AND d.{activeColumn} = {columns.InactiveValue}");
        sb.AppendLine($"        AND c.{fkColumns[0]} = i.{referencedColumns[0]}");
        sb.AppendLine($"        AND ABS(DATEDIFF(MILLISECOND, c.{childValidToColumn}, d.{validToColumn})) <= {toleranceMs}");
        sb.AppendLine("    )");
        sb.AppendLine($"    AND c.{childActiveColumn} = {columns.InactiveValue};  -- Only reactivate inactive children");
      }
      else
      {
        // Multi-column FK: use EXISTS with correlated subquery
        sb.AppendLine($"    UPDATE c");
        sb.AppendLine("    SET");
        sb.AppendLine($"        c.{childActiveColumn} = {columns.ActiveValue},");
        sb.AppendLine($"        c.{columns.UpdatedBy} = @updated_by");
        sb.AppendLine($"    FROM [{childSchema}].[{childName}] c");
        sb.AppendLine("    WHERE EXISTS (");
        sb.AppendLine("        SELECT 1");
        sb.AppendLine("        FROM inserted i");
        sb.AppendLine($"        JOIN deleted d ON {BuildPkJoinCondition(primaryKeyColumns, "i", "d")}");
        sb.AppendLine($"        WHERE i.{activeColumn} = {columns.ActiveValue} AND d.{activeColumn} = {columns.InactiveValue}");
        sb.AppendLine($"        AND {BuildFkJoinCondition(fkColumns, referencedColumns, "c", "i")}");
        sb.AppendLine($"        AND ABS(DATEDIFF(MILLISECOND, c.{childValidToColumn}, d.{validToColumn})) <= {toleranceMs}");
        sb.AppendLine("    )");
        sb.AppendLine($"    AND c.{childActiveColumn} = {columns.InactiveValue};  -- Only reactivate inactive children");
      }
      sb.AppendLine();
    }

    sb.AppendLine("END;");
    sb.AppendLine("GO");

    return sb.ToString();
  }
}
