using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.SqlServer.Dac.Model;
using SchemaTools.Models;
using DacCheckConstraint = Microsoft.SqlServer.Dac.Model.CheckConstraint;
using DacFKConstraint = Microsoft.SqlServer.Dac.Model.ForeignKeyConstraint;
// DacFx type aliases
using DacPKConstraint = Microsoft.SqlServer.Dac.Model.PrimaryKeyConstraint;
using DacUniqueConstraint = Microsoft.SqlServer.Dac.Model.UniqueConstraint;
using ModelCheckConstraint = SchemaTools.Models.CheckConstraint;
using ModelFKConstraint = SchemaTools.Models.ForeignKeyConstraint;
// Aliases to disambiguate from DacFx types
using ModelPKConstraint = SchemaTools.Models.PrimaryKeyConstraint;
using ModelUniqueConstraint = SchemaTools.Models.UniqueConstraint;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Post-build MSBuild task that extracts authoritative schema metadata from a compiled .dacpac.
/// This runs AFTER SqlBuild and provides the definitive schema for validation and documentation.
/// </summary>
public class SchemaMetadataExtractor : MSTask
{
  /// <summary>
  /// Path to the compiled .dacpac file (typically $(TargetPath))
  /// </summary>
  [Required]
  public string DacpacPath { get; set; } = string.Empty;

  /// <summary>
  /// Output file for extracted metadata JSON
  /// </summary>
  [Required]
  public string OutputFile { get; set; } = string.Empty;

  /// <summary>
  /// Configuration file path
  /// </summary>
  public string ConfigFile { get; set; } = string.Empty;

  /// <summary>
  /// Database name for metadata
  /// </summary>
  public string DatabaseName { get; set; } = "Database";

  // Testing support
  internal SchemaToolsConfig? TestConfig { get; set; }
  internal TSqlModel? TestModel { get; set; }

  private SchemaToolsConfig _config = new();

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Schema Metadata Extractor (Post-Build)");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, string.Empty);

      LoadConfiguration();

      // Load the compiled model
      TSqlModel model = LoadModel();

      Log.LogMessage(MessageImportance.High, $"Extracting metadata from: {Path.GetFileName(DacpacPath)}");

      var metadata = new SchemaMetadata
      {
        Version = GetAssemblyVersion(),
        GeneratedAt = DateTime.UtcNow,
        GeneratedBy = "SchemaMetadataExtractor (DacFx)",
        Database = _config.Database ?? DatabaseName,
        DefaultSchema = _config.DefaultSchema,
        SqlServerVersion = GetSqlServerVersion(model)
      };

      // Extract all tables
      IEnumerable<TSqlObject> tables = model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass);
      int tableCount = 0;
      int totalColumns = 0;
      int totalConstraints = 0;

      foreach (TSqlObject table in tables)
      {
        TableMetadata tableMeta = ExtractTableMetadata(table);
        metadata.Tables.Add(tableMeta);
        tableCount++;
        totalColumns += tableMeta.Columns.Count;
        totalConstraints += tableMeta.Constraints.ForeignKeys.Count;
        totalConstraints += tableMeta.Constraints.UniqueConstraints.Count;
        totalConstraints += tableMeta.Constraints.PrimaryKey != null ? 1 : 0;
      }

      // Build FK dependency graph
      ResolveForeignKeyGraph(metadata);

      // Detect patterns using config
      foreach (TableMetadata table in metadata.Tables)
      {
        DetectTablePatterns(table, _config);
      }

      // Calculate statistics
      metadata.Statistics = new SchemaStatistics
      {
        TotalTables = tableCount,
        TemporalTables = metadata.Tables.Count(t => t.HasTemporalVersioning),
        SoftDeleteTables = metadata.Tables.Count(t => t.HasSoftDelete),
        AppendOnlyTables = metadata.Tables.Count(t => t.IsAppendOnly),
        PolymorphicTables = metadata.Tables.Count(t => t.IsPolymorphic),
        TotalColumns = totalColumns,
        TotalConstraints = totalConstraints
      };

      metadata.Categories = _config.Categories;

      // Ensure output directory exists
      string? outputDir = Path.GetDirectoryName(OutputFile);
      if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      // Serialise
      var options = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      };

      string json = JsonSerializer.Serialize(metadata, options);
      File.WriteAllText(OutputFile, json, System.Text.Encoding.UTF8);

      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Extraction Summary");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, $"Tables:            {metadata.Statistics.TotalTables}");
      Log.LogMessage(MessageImportance.High, $"Temporal:          {metadata.Statistics.TemporalTables}");
      Log.LogMessage(MessageImportance.High, $"Soft-delete:       {metadata.Statistics.SoftDeleteTables}");
      Log.LogMessage(MessageImportance.High, $"Total columns:     {metadata.Statistics.TotalColumns}");
      Log.LogMessage(MessageImportance.High, $"Total constraints: {metadata.Statistics.TotalConstraints}");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, $"+ Metadata written to: {OutputFile}");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Metadata extraction failed: {ex.Message}");
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
  }

  private TSqlModel LoadModel()
  {
    if (TestModel != null)
    {
      return TestModel;
    }

    if (!File.Exists(DacpacPath))
    {
      throw new FileNotFoundException($"Dacpac not found: {DacpacPath}");
    }

    return TSqlModel.LoadFromDacpac(DacpacPath, new ModelLoadOptions
    {
      LoadAsScriptBackedModel = true,
      ThrowOnModelErrors = false
    });
  }

  private TableMetadata ExtractTableMetadata(TSqlObject table)
  {
    ObjectIdentifier tableName = table.Name;
    string schema = tableName.Parts.Count > 1 ? tableName.Parts[0] : _config.DefaultSchema;
    string name = tableName.Parts.Count > 1 ? tableName.Parts[1] : tableName.Parts[0];

    var metadata = new TableMetadata
    {
      Name = name,
      Schema = schema
    };

    // Extract columns
    IEnumerable<TSqlObject> columns = table.GetReferenced(Table.Columns);
    foreach (TSqlObject column in columns)
    {
      ColumnMetadata colMeta = ExtractColumnMetadata(column);
      metadata.Columns.Add(colMeta);

      // Check for active column
      if (string.Equals(colMeta.Name, _config.Columns.Active, StringComparison.OrdinalIgnoreCase))
      {
        metadata.HasActiveColumn = true;
      }

      // Check for primary key
      if (colMeta.IsPrimaryKey)
      {
        metadata.PrimaryKey = colMeta.Name;
        metadata.PrimaryKeyType = colMeta.Type;
      }
    }

    // Check for temporal versioning (detect by checking for history table relationship)
    TSqlObject? historyTable = table.GetReferenced(Table.TemporalSystemVersioningHistoryTable).FirstOrDefault();
    metadata.HasTemporalVersioning = historyTable != null;

    if (metadata.HasTemporalVersioning && historyTable != null)
    {
      if (historyTable != null)
      {
        ObjectIdentifier historyName = historyTable.Name;
        string historySchema = historyName.Parts.Count > 1 ? historyName.Parts[0] : _config.DefaultSchema;
        string historyTableName = historyName.Parts.Count > 1 ? historyName.Parts[1] : historyName.Parts[0];
        metadata.HistoryTable = $"[{historySchema}].[{historyTableName}]";
      }
    }

    // Extract primary key constraint
    IEnumerable<TSqlObject> pkConstraints = table.GetReferencing(DacPKConstraint.Host);
    foreach (TSqlObject pk in pkConstraints)
    {
      IEnumerable<TSqlObject> pkColumns = pk.GetReferenced(DacPKConstraint.Columns);
      metadata.Constraints.PrimaryKey = new ModelPKConstraint
      {
        Name = pk.Name.Parts.LastOrDefault() ?? $"PK_{name}",
        Columns = pkColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
        IsClustered = pk.GetProperty<bool?>(DacPKConstraint.Clustered) == true
      };
    }

    // Extract foreign key constraints
    IEnumerable<TSqlObject> fkConstraints = table.GetReferencing(DacFKConstraint.Host);
    foreach (TSqlObject fk in fkConstraints)
    {
      IEnumerable<TSqlObject> fkColumns = fk.GetReferenced(DacFKConstraint.Columns);
      TSqlObject? refTable = fk.GetReferenced(DacFKConstraint.ForeignTable).FirstOrDefault();
      IEnumerable<TSqlObject> refColumns = fk.GetReferenced(DacFKConstraint.ForeignColumns);

      if (refTable != null)
      {
        ObjectIdentifier refTableName = refTable.Name;
        string refSchema = refTableName.Parts.Count > 1 ? refTableName.Parts[0] : _config.DefaultSchema;
        string refName = refTableName.Parts.Count > 1 ? refTableName.Parts[1] : refTableName.Parts[0];

        metadata.Constraints.ForeignKeys.Add(new ModelFKConstraint
        {
          Name = fk.Name.Parts.LastOrDefault() ?? $"FK_{name}",
          Columns = fkColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
          ReferencedTable = refName,
          ReferencedSchema = refSchema,
          ReferencedColumns = refColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
          OnDelete = fk.GetProperty<ForeignKeyAction?>(DacFKConstraint.DeleteAction)?.ToString() ?? "NoAction",
          OnUpdate = fk.GetProperty<ForeignKeyAction?>(DacFKConstraint.UpdateAction)?.ToString() ?? "NoAction"
        });
      }
    }

    // Extract unique constraints
    IEnumerable<TSqlObject> uniqueConstraints = table.GetReferencing(DacUniqueConstraint.Host);
    foreach (TSqlObject uc in uniqueConstraints)
    {
      IEnumerable<TSqlObject> ucColumns = uc.GetReferenced(DacUniqueConstraint.Columns);
      metadata.Constraints.UniqueConstraints.Add(new ModelUniqueConstraint
      {
        Name = uc.Name.Parts.LastOrDefault() ?? $"UQ_{name}",
        Columns = ucColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
        IsClustered = uc.GetProperty<bool?>(DacUniqueConstraint.Clustered) == true
      });
    }

    // Extract check constraints
    IEnumerable<TSqlObject> checkConstraints = table.GetReferencing(DacCheckConstraint.Host);
    foreach (TSqlObject cc in checkConstraints)
    {
      string? expression = cc.GetProperty<string>(DacCheckConstraint.Expression);
      metadata.Constraints.CheckConstraints.Add(new ModelCheckConstraint
      {
        Name = cc.Name.Parts.LastOrDefault() ?? $"CK_{name}",
        Expression = expression ?? ""
      });
    }

    return metadata;
  }

  private ColumnMetadata ExtractColumnMetadata(TSqlObject column)
  {
    string name = column.Name.Parts.LastOrDefault() ?? "";

    // Get data type
    TSqlObject? dataType = column.GetReferenced(Column.DataType).FirstOrDefault();
    string typeName = dataType?.Name.Parts.LastOrDefault() ?? "unknown";

    // Get length/precision info
    int? length = column.GetProperty<int?>(Column.Length);
    int? precision = column.GetProperty<int?>(Column.Precision);
    int? scale = column.GetProperty<int?>(Column.Scale);

    string typeStr = typeName;
    if (length.HasValue && length > 0)
    {
      typeStr = length == -1 ? $"{typeName}(max)" : $"{typeName}({length})";
    }
    else if (precision.HasValue && precision > 0)
    {
      typeStr = scale.HasValue && scale > 0 ? $"{typeName}({precision},{scale})" : $"{typeName}({precision})";
    }

    return new ColumnMetadata
    {
      Name = name,
      Type = typeStr,
      Nullable = column.GetProperty<bool?>(Column.Nullable) ?? true,
      IsPrimaryKey = false, // Set by constraint analysis
      IsIdentity = column.GetProperty<bool?>(Column.IsIdentity) == true,
      // Note: Computed column detection deferred - DacFx doesn't expose simple property
      IsComputed = false
    };
  }

  private static void ResolveForeignKeyGraph(SchemaMetadata metadata)
  {
    // Build lookup
    var tablesByName = metadata.Tables.ToDictionary(
      t => $"[{t.Schema}].[{t.Name}]",
      t => t,
      StringComparer.OrdinalIgnoreCase);

    // Mark columns with FK references
    foreach (TableMetadata table in metadata.Tables)
    {
      foreach (ModelFKConstraint fk in table.Constraints.ForeignKeys)
      {
        string fkSchema = fk.ReferencedSchema ?? metadata.DefaultSchema;

        // Set column-level FK reference
        if (fk.Columns.Count == 1)
        {
          ColumnMetadata? col = table.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, fk.Columns[0], StringComparison.OrdinalIgnoreCase));

          if (col != null && col.ForeignKey == null)
          {
            col.ForeignKey = new ForeignKeyReference
            {
              Table = fk.ReferencedTable,
              Column = fk.ReferencedColumns.FirstOrDefault() ?? "id",
              Schema = fkSchema
            };
          }
        }
      }
    }
  }

  private static void DetectTablePatterns(TableMetadata table, SchemaToolsConfig config)
  {
    SchemaToolsConfig effective = config.ResolveForTable(table.Name, table.Category);

    // Set ActiveColumnName if the table has an active column
    if (table.HasActiveColumn)
    {
      table.ActiveColumnName = effective.Columns.Active;
    }

    // Soft-delete detection
    if (effective.Features.EnableSoftDelete &&
        table.HasActiveColumn &&
        table.HasTemporalVersioning)
    {
      table.HasSoftDelete = true;
    }

    // Append-only detection
    if (effective.Features.DetectAppendOnlyTables)
    {
      bool hasCreatedAt = table.Columns.Any(c =>
        string.Equals(c.Name, effective.Columns.CreatedAt, StringComparison.OrdinalIgnoreCase));
      bool hasUpdatedBy = table.Columns.Any(c =>
        string.Equals(c.Name, effective.Columns.UpdatedBy, StringComparison.OrdinalIgnoreCase));

      if (hasCreatedAt && !hasUpdatedBy && !table.HasTemporalVersioning)
      {
        table.IsAppendOnly = true;
      }
    }

    // Polymorphic detection
    if (effective.Features.DetectPolymorphicPatterns)
    {
      foreach (PolymorphicPatternConfig pattern in effective.Columns.PolymorphicPatterns)
      {
        bool hasTypeCol = table.Columns.Any(c =>
          string.Equals(c.Name, pattern.TypeColumn, StringComparison.OrdinalIgnoreCase));
        bool hasIdCol = table.Columns.Any(c =>
          string.Equals(c.Name, pattern.IdColumn, StringComparison.OrdinalIgnoreCase));

        if (hasTypeCol && hasIdCol)
        {
          table.IsPolymorphic = true;
          table.PolymorphicOwner = new PolymorphicOwnerInfo
          {
            TypeColumn = pattern.TypeColumn,
            IdColumn = pattern.IdColumn
          };
          break;
        }
      }
    }
  }

  private static string GetSqlServerVersion(TSqlModel model)
  {
    SqlServerVersion version = model.Version;
    return version.ToString();
  }

  private static string GetAssemblyVersion()
  {
    return typeof(SchemaMetadataExtractor).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
      ?? typeof(SchemaMetadataExtractor).Assembly.GetName().Version?.ToString()
      ?? "0.0.0";
  }
}
