using System.Text;
using System.Text.Json;
using SchemaTools.Models;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Generates hard-delete triggers from schema metadata
/// </summary>
public class SqlTriggerGenerator : MSTask
{
  [Microsoft.Build.Framework.Required]
  public string MetadataFile { get; set; } = string.Empty;

  [Microsoft.Build.Framework.Required]
  public string OutputDirectory { get; set; } = string.Empty;

  public string CustomTriggersDirectory { get; set; } = string.Empty;

  public bool Force { get; set; }

  // Allow injecting metadata for testing
  internal SchemaMetadata? TestMetadata { get; set; }

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "════════════════════════════════════════════════════════");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "  Hard Delete Trigger Generator");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "════════════════════════════════════════════════════════");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      // Load metadata
      SchemaMetadata? metadata = LoadMetadata();
      if (metadata == null)
      {
        return false;
      }

      Log.LogMessage($"Loaded metadata for {metadata.Tables.Count} tables");

      // Filter tables that need triggers
      var triggerTables = metadata.Tables
          .Where(t => t.Triggers.HardDelete.Generate)
          .ToList();

      if (triggerTables.Count == 0)
      {
        Log.LogMessage("No tables require hard-delete triggers (feature may be disabled in config)");
        return true;
      }

      Log.LogMessage($"Found {triggerTables.Count} tables requiring hard-delete triggers");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      // Ensure output directory exists
      Directory.CreateDirectory(OutputDirectory);

      int generated = 0;
      int skipped = 0;

      foreach (TableMetadata? table in triggerTables)
      {
        string triggerName = table.Triggers.HardDelete.Name ?? $"trg_{table.Name}_hard_delete";
        string fileName = $"{triggerName}.sql";
        string filePath = Path.Combine(OutputDirectory, fileName);

        // Check for custom trigger override
        if (!string.IsNullOrEmpty(CustomTriggersDirectory))
        {
          string customPath = Path.Combine(CustomTriggersDirectory, fileName);
          if (File.Exists(customPath))
          {
            Log.LogMessage($"Skipped {table.Name}: Custom trigger exists");
            skipped++;
            continue;
          }
        }

        // Check if already exists and Force not set
        if (File.Exists(filePath) && !Force)
        {
          Log.LogMessage(Microsoft.Build.Framework.MessageImportance.Low, $"Skipped {table.Name}: Already exists (use Force=true to regenerate)");
          skipped++;
          continue;
        }

        // Generate trigger
        string primaryKey = table.PrimaryKey ?? "id";
        string activeColumn = table.Triggers.HardDelete.ActiveColumnName;
        string triggerSql = GenerateTriggerSql(table.Schema, table.Name, triggerName, primaryKey, activeColumn);
        File.WriteAllText(filePath, triggerSql, Encoding.UTF8);

        Log.LogMessage($"✓ Generated: {fileName}");
        generated++;
      }

      // Summary
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "════════════════════════════════════════════════════════");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "  Summary");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "════════════════════════════════════════════════════════");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Generated:  {generated}");
      if (skipped > 0)
      {
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Skipped:    {skipped}");
      }
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Output dir: {OutputDirectory}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "════════════════════════════════════════════════════════");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Failed to generate triggers: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private SchemaMetadata? LoadMetadata()
  {
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

  private static string GenerateTriggerSql(string schemaName, string tableName, string triggerName,
      string primaryKey, string activeColumn)
  {
    return $@"-- ═══════════════════════════════════════════════════════════════════════════════
-- Hard Delete Trigger for [{schemaName}].[{tableName}]
-- Generated by: SqlTriggerGenerator MSBuild Task
-- Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
--
-- Purpose:
--   Immediately performs hard delete when the '{activeColumn}' flag is set to 0.
--   This implements soft-delete with immediate cleanup while preserving
--   complete audit trail in temporal history table.
--
-- DO NOT EDIT MANUALLY
-- To regenerate: Build the project or run with Force=true
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE TRIGGER [{schemaName}].[{triggerName}]
ON [{schemaName}].[{tableName}]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- When '{activeColumn}' flag changes from 1 to 0 (soft delete)
    -- Immediately perform hard delete to keep table clean
    IF UPDATE({activeColumn})
    BEGIN
        DELETE FROM [{schemaName}].[{tableName}]
        WHERE {primaryKey} IN (
            SELECT i.{primaryKey}
            FROM inserted i
            JOIN deleted d ON i.{primaryKey} = d.{primaryKey}
            WHERE i.{activeColumn} = 0 AND d.{activeColumn} = 1
        );
    END
END;
GO
";
  }
}
