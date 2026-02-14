using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Models;
using SchemaTools.Utilities;
using SchemaTools.Visitors;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Pre-build MSBuild task that analyses SQL source files to extract patterns 
/// needed for code generation (triggers, procedures). This runs BEFORE SqlBuild.
/// For authoritative metadata extraction, use SchemaMetadataExtractor post-build.
/// </summary>
public class SchemaSourceAnalyser : MSTask
{
  /// <summary>
  /// SQL files to analyse (typically @(Build) items from the SQL project)
  /// </summary>
  public ITaskItem[]? SqlFiles { get; set; }

  /// <summary>
  /// Fallback directory to scan if SqlFiles is not provided
  /// </summary>
  public string TablesDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Output file for analysis results (intermediate, used by code generators)
  /// </summary>
  [Required]
  public string AnalysisOutput { get; set; } = string.Empty;

  /// <summary>
  /// Directory where generated triggers are placed. Files in this directory
  /// are considered "owned" by SchemaTools and can be regenerated.
  /// </summary>
  public string GeneratedTriggersDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Configuration file path
  /// </summary>
  public string ConfigFile { get; set; } = string.Empty;

  // Fallback defaults (used when config file not provided)
  public string SqlServerVersion { get; set; } = "Sql160";
  public string DefaultSchema { get; set; } = "dbo";

  // Testing support
  internal SchemaToolsConfig? TestConfig { get; set; }

  private SchemaToolsConfig _config = new();

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Schema Source Analyser (Pre-Build)");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, string.Empty);

      LoadConfiguration();

      List<string> sqlFiles = ResolveSqlFiles();
      if (sqlFiles.Count == 0)
      {
        Log.LogWarning("No SQL files to analyse");
        return true;
      }

      Log.LogMessage(MessageImportance.High, $"Analysing {sqlFiles.Count} SQL file(s)...");

      TSqlParser parser = CreateParser(_config.SqlServerVersion);
      var analysis = new SourceAnalysisResult
      {
        Version = GetAssemblyVersion(),
        AnalysedAt = DateTime.UtcNow,
        SqlServerVersion = _config.SqlServerVersion,
        DefaultSchema = _config.DefaultSchema,
        GeneratedTriggersDirectory = GeneratedTriggersDirectory,
        Columns = new ColumnConfig
        {
          Active = _config.Columns.Active,
          ActiveValue = _config.Columns.ActiveValue,
          InactiveValue = _config.Columns.InactiveValue,
          UpdatedBy = _config.Columns.UpdatedBy,
          UpdatedByType = _config.Columns.UpdatedByType,
          ValidFrom = _config.Columns.ValidFrom,
          ValidTo = _config.Columns.ValidTo
        },
        Features = new FeatureFlags
        {
          GenerateReactivationGuards = _config.Features.GenerateReactivationGuards
        }
      };

      int tablesParsed = 0;

      foreach (string sqlFile in sqlFiles)
      {
        try
        {
          // Analyse tables
          TableAnalysis? tableAnalysis = AnalyseTableFile(sqlFile, parser);
          if (tableAnalysis != null)
          {
            analysis.Tables.Add(tableAnalysis);
            tablesParsed++;
          }

          // Also discover existing triggers in this file
          DiscoverExistingTriggers(sqlFile, parser, analysis);
        }
        catch (Exception ex)
        {
          Log.LogWarning($"Failed to analyse {Path.GetFileName(sqlFile)}: {ex.Message}");
        }
      }

      // Build FK dependency graph for cascade analysis
      BuildForeignKeyGraph(analysis);

      // Detect leaf tables (tables with no children referencing them)
      DetectLeafTables(analysis);

      // Ensure output directory exists
      string? outputDir = Path.GetDirectoryName(AnalysisOutput);
      if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      // Serialise analysis
      var options = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      };

      string json = JsonSerializer.Serialize(analysis, options);
      File.WriteAllText(AnalysisOutput, json, System.Text.Encoding.UTF8);

      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, $"+ Analysed {tablesParsed} tables");
      Log.LogMessage(MessageImportance.High, $"  Soft-delete: {analysis.Tables.Count(t => t.HasSoftDelete)}");
      Log.LogMessage(MessageImportance.High, $"  Leaf tables: {analysis.Tables.Count(t => t.IsLeafTable)}");
      int explicitTriggers = analysis.ExistingTriggers.Count(t => !t.IsGenerated);
      if (explicitTriggers > 0)
      {
        Log.LogMessage(MessageImportance.High, $"  Explicit triggers: {explicitTriggers}");
      }
      Log.LogMessage(MessageImportance.High, $"  Output: {AnalysisOutput}");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Source analysis failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private void LoadConfiguration()
  {
    if (TestConfig != null)
    {
      _config = TestConfig;
      return;
    }

    if (!string.IsNullOrEmpty(ConfigFile) && File.Exists(ConfigFile))
    {
      string json = File.ReadAllText(ConfigFile);
      _config = JsonSerializer.Deserialize<SchemaToolsConfig>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      }) ?? new SchemaToolsConfig();
    }
    else
    {
      _config = new SchemaToolsConfig
      {
        SqlServerVersion = SqlServerVersion,
        DefaultSchema = DefaultSchema
      };
    }
  }

  private List<string> ResolveSqlFiles()
  {
    // Explicit file list takes precedence (from @(Build) items)
    if (SqlFiles != null && SqlFiles.Length > 0)
    {
      return SqlFiles
        .Select(item => item.GetMetadata("FullPath"))
        .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
        .OrderBy(f => f)
        .ToList();
    }

    // Fallback: scan directory
    if (!string.IsNullOrEmpty(TablesDirectory) && Directory.Exists(TablesDirectory))
    {
      return Directory.GetFiles(TablesDirectory, "*.sql", SearchOption.TopDirectoryOnly)
        .OrderBy(f => f)
        .ToList();
    }

    return new List<string>();
  }

  private static TSqlParser CreateParser(string version)
  {
    return version switch
    {
      "Sql100" => new TSql100Parser(initialQuotedIdentifiers: true),
      "Sql110" => new TSql110Parser(initialQuotedIdentifiers: true),
      "Sql120" => new TSql120Parser(initialQuotedIdentifiers: true),
      "Sql130" => new TSql130Parser(initialQuotedIdentifiers: true),
      "Sql140" => new TSql140Parser(initialQuotedIdentifiers: true),
      "Sql150" => new TSql150Parser(initialQuotedIdentifiers: true),
      "Sql160" => new TSql160Parser(initialQuotedIdentifiers: true),
      _ => new TSql160Parser(initialQuotedIdentifiers: true)
    };
  }

  private TableAnalysis? AnalyseTableFile(string filePath, TSqlParser parser)
  {
    string sqlText = File.ReadAllText(filePath);

    using var reader = new StringReader(sqlText);
    TSqlFragment fragment = parser.Parse(reader, out IList<ParseError>? errors);

    if (errors != null && errors.Count > 0)
    {
      Log.LogMessage(MessageImportance.Low, $"Parse warnings in {Path.GetFileName(filePath)}");
    }

    var visitor = new TableMetadataVisitor();
    fragment.Accept(visitor);

    if (string.IsNullOrEmpty(visitor.TableName))
    {
      return null;
    }

    string? category = SqlCommentParser.ExtractCategory(sqlText);
    SchemaToolsConfig effective = _config.ResolveForTable(visitor.TableName!, category);

    var analysis = new TableAnalysis
    {
      Name = visitor.TableName!,
      Schema = visitor.SchemaName ?? effective.DefaultSchema,
      Category = category,
      SourceFile = filePath
    };

    // Detect active column (soft-delete indicator)
    string activeColumnName = effective.Columns.Active;
    bool hasActiveColumn = visitor.ColumnDefinitions.Any(c =>
      string.Equals(c.ColumnIdentifier.Value, activeColumnName, StringComparison.OrdinalIgnoreCase));

    analysis.HasActiveColumn = hasActiveColumn;
    analysis.HasTemporalVersioning = visitor.HasTemporalVersioning;

    // Populate temporal table details if detected
    if (visitor.HasTemporalVersioning)
    {
      string historySchema = visitor.HistorySchemaName ?? effective.DefaultSchema;
      if (!string.IsNullOrEmpty(visitor.HistoryTableName))
      {
        analysis.HistoryTable = $"[{historySchema}].[{visitor.HistoryTableName}]";
      }
      analysis.ValidFromColumn = effective.Columns.ValidFrom;
      analysis.ValidToColumn = effective.Columns.ValidTo;
    }

    // Soft-delete = has active column + temporal versioning
    if (effective.Features.EnableSoftDelete && hasActiveColumn && visitor.HasTemporalVersioning)
    {
      analysis.HasSoftDelete = true;
      analysis.ActiveColumnName = activeColumnName;
      analysis.SoftDeleteMode = effective.Features.SoftDeleteMode;
    }

    // Extract primary key
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      if (constraint is UniqueConstraintDefinition unique && unique.IsPrimaryKey)
      {
        analysis.PrimaryKeyColumns = unique.Columns
          .Select(c => c.Column.MultiPartIdentifier.Identifiers.Last().Value)
          .ToList();
        break;
      }
    }

    // Also check inline PK constraints
    if (analysis.PrimaryKeyColumns.Count == 0)
    {
      foreach (ColumnDefinition colDef in visitor.ColumnDefinitions)
      {
        foreach (ConstraintDefinition constraint in colDef.Constraints)
        {
          if (constraint is UniqueConstraintDefinition { IsPrimaryKey: true })
          {
            analysis.PrimaryKeyColumns.Add(colDef.ColumnIdentifier.Value);
            break;
          }
        }
      }
    }

    // Extract FK references (for dependency graph)
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      if (constraint is ForeignKeyConstraintDefinition fk)
      {
        analysis.ForeignKeyReferences.Add(new ForeignKeyRef
        {
          ReferencedTable = fk.ReferenceTableName.BaseIdentifier.Value,
          ReferencedSchema = fk.ReferenceTableName.SchemaIdentifier?.Value ?? _config.DefaultSchema,
          Columns = fk.Columns.Select(c => c.Value).ToList(),
          ReferencedColumns = fk.ReferencedTableColumns.Select(c => c.Value).ToList(),
          OnDelete = fk.DeleteAction.ToString()
        });
      }
    }

    return analysis;
  }

  private void DiscoverExistingTriggers(string filePath, TSqlParser parser, SourceAnalysisResult analysis)
  {
    string sqlText = File.ReadAllText(filePath);
    using var reader = new StringReader(sqlText);
    TSqlFragment fragment = parser.Parse(reader, out _);

    var visitor = new TriggerDiscoveryVisitor();
    fragment.Accept(visitor);

    if (visitor.Triggers.Count == 0)
    {
      return;
    }

    // Determine if this file is in the generated directory
    string normalisedPath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
    string normalisedGenDir = string.IsNullOrEmpty(GeneratedTriggersDirectory)
      ? string.Empty
      : Path.GetFullPath(GeneratedTriggersDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    bool isInGeneratedDir = !string.IsNullOrEmpty(normalisedGenDir) &&
                            normalisedPath.StartsWith(normalisedGenDir, StringComparison.OrdinalIgnoreCase);

    foreach (DiscoveredTrigger trigger in visitor.Triggers)
    {
      analysis.ExistingTriggers.Add(new ExistingTrigger
      {
        Name = trigger.Name,
        Schema = trigger.Schema ?? _config.DefaultSchema,
        TargetTable = trigger.TargetTable ?? string.Empty,
        SourceFile = filePath,
        IsGenerated = isInGeneratedDir
      });

      if (!isInGeneratedDir)
      {
        Log.LogMessage(MessageImportance.Low,
          $"  Discovered explicit trigger: [{trigger.Schema ?? _config.DefaultSchema}].[{trigger.Name}] in {Path.GetFileName(filePath)}");
      }
    }
  }

  private static void BuildForeignKeyGraph(SourceAnalysisResult analysis)
  {
    // Build lookup for quick access
    var tablesByName = analysis.Tables.ToDictionary(
      t => $"[{t.Schema}].[{t.Name}]",
      t => t,
      StringComparer.OrdinalIgnoreCase);

    // For each FK, record the parent table's children
    foreach (TableAnalysis table in analysis.Tables)
    {
      foreach (ForeignKeyRef fkRef in table.ForeignKeyReferences)
      {
        string parentKey = $"[{fkRef.ReferencedSchema}].[{fkRef.ReferencedTable}]";
        if (tablesByName.TryGetValue(parentKey, out TableAnalysis? parentTable))
        {
          parentTable.ChildTables.Add(table.Name);
        }
      }
    }
  }

  private static void DetectLeafTables(SourceAnalysisResult analysis)
  {
    // Leaf tables are tables that have no children referencing them
    foreach (TableAnalysis table in analysis.Tables)
    {
      table.IsLeafTable = table.ChildTables.Count == 0;
    }
  }

  private static string GetAssemblyVersion()
  {
    return typeof(SchemaSourceAnalyser).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
      ?? typeof(SchemaSourceAnalyser).Assembly.GetName().Version?.ToString()
      ?? "0.0.0";
  }
}

/// <summary>
/// Pre-build source analysis result - used for code generation decisions
/// </summary>
public class SourceAnalysisResult
{
  public string Version { get; set; } = "1.0.0";
  public DateTime AnalysedAt { get; set; }
  public string SqlServerVersion { get; set; } = "Sql160";
  public string DefaultSchema { get; set; } = "dbo";
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
  /// The generated triggers directory path (used to distinguish explicit vs generated)
  /// </summary>
  public string GeneratedTriggersDirectory { get; set; } = string.Empty;
}

/// <summary>
/// Column configuration for code generation
/// </summary>
public class ColumnConfig
{
  public string Active { get; set; } = "active";
  public string ActiveValue { get; set; } = "1";
  public string InactiveValue { get; set; } = "0";
  public string UpdatedBy { get; set; } = "updated_by";
  public string UpdatedByType { get; set; } = "UNIQUEIDENTIFIER";
  public string ValidFrom { get; set; } = "valid_from";
  public string ValidTo { get; set; } = "valid_to";
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
  public string Schema { get; set; } = "dbo";
  public string TargetTable { get; set; } = string.Empty;
  public string SourceFile { get; set; } = string.Empty;

  /// <summary>
  /// True if this trigger is in the _generated directory (owned by SchemaTools)
  /// </summary>
  public bool IsGenerated { get; set; }
}

/// <summary>
/// Analysis result for a single table - focused on code generation needs
/// </summary>
public class TableAnalysis
{
  public string Name { get; set; } = string.Empty;
  public string Schema { get; set; } = "dbo";
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

  // Code generation flags
  public bool GenerateTrigger { get; set; }
  public string? TriggerName { get; set; }

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
  public string ReferencedSchema { get; set; } = "dbo";
  public List<string> Columns { get; set; } = new();
  public List<string> ReferencedColumns { get; set; } = new();
  public string OnDelete { get; set; } = "NoAction";
}
