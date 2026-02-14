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
/// MSBuild task to extract schema metadata by parsing SQL table files directly.
/// 
/// Use this when you need metadata WITHOUT building a .dacpac, such as:
/// - Offline documentation generation
/// - Development/testing workflows
/// - CI pipelines that don't require full compilation
/// 
/// For authoritative metadata from compiled schema, use SchemaMetadataExtractor post-build.
/// </summary>
public class SchemaMetadataGenerator : MSTask
{
  public string TablesDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Explicit list of SQL files to process (takes precedence over TablesDirectory)
  /// </summary>
  public ITaskItem[]? TableFiles { get; set; }

  [Required]
  public string OutputFile { get; set; } = string.Empty;

  public string ConfigFile { get; set; } = string.Empty;

  // Fallback defaults (used when config file not provided)
  public string SqlServerVersion { get; set; } = "Sql160";
  public string DefaultSchema { get; set; } = "dbo";
  public string DatabaseName { get; set; } = "Database";

  // Allow injecting config for testing
  internal SchemaToolsConfig? TestConfig { get; set; }

  private SchemaToolsConfig _config = new();

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "  Schema Metadata Generator");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      // Load configuration
      LoadConfiguration();

      // Debug logging
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.Low,
          $"Configuration: EnableSoftDelete={_config.Features.EnableSoftDelete}, " +
          $"DetectPolymorphic={_config.Features.DetectPolymorphicPatterns}");

      // Resolve SQL files: explicit list takes precedence over directory scan
      List<string> sqlFiles = ResolveSqlFiles();
      if (sqlFiles.Count == 0)
        return false;

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          $"Found {sqlFiles.Count} SQL table file(s)");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      // Create parser
      TSqlParser parser = CreateParser(_config.SqlServerVersion);

      // Parse all files
      var tableMetadata = new List<TableMetadata>();
      int parseErrors = 0;
      int totalColumns = 0;
      int totalConstraints = 0;
      int current = 0;

      foreach (string? sqlFile in sqlFiles)
      {
        current++;
        string fileName = Path.GetFileNameWithoutExtension(sqlFile);
        Log.LogMessage($"[{current,3}/{sqlFiles.Count}] Parsing: {fileName}.sql");

        try
        {
          TableMetadata? metadata = ParseTableFile(sqlFile, parser);
          if (metadata != null)
          {
            tableMetadata.Add(metadata);
            totalColumns += metadata.Columns.Count;
            totalConstraints += metadata.Constraints.PrimaryKey != null ? 1 : 0;
            totalConstraints += metadata.Constraints.ForeignKeys.Count;
            totalConstraints += metadata.Constraints.UniqueConstraints.Count;
            totalConstraints += metadata.Constraints.CheckConstraints.Count;

            Log.LogMessage(Microsoft.Build.Framework.MessageImportance.Low,
                $"         + {metadata.Columns.Count} columns, " +
                $"{metadata.Constraints.ForeignKeys.Count} FKs, " +
                $"HasActive={metadata.HasActiveColumn}, HasTemporal={metadata.HasTemporalVersioning}, " +
                $"HasSoftDelete={metadata.HasSoftDelete}, IsPolymorphic={metadata.IsPolymorphic}");
          }
        }
        catch (Exception ex)
        {
          Log.LogWarning($"         ! Failed to parse {fileName}: {ex.Message}");
          parseErrors++;
        }
      }

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      if (parseErrors > 0)
      {
        Log.LogWarning($"! {parseErrors} file(s) had parse errors");
      }

      // Calculate statistics
      var stats = new SchemaStatistics
      {
        TotalTables = tableMetadata.Count,
        TemporalTables = tableMetadata.Count(t => t.HasTemporalVersioning),
        SoftDeleteTables = tableMetadata.Count(t => t.HasSoftDelete),
        AppendOnlyTables = tableMetadata.Count(t => t.IsAppendOnly),
        PolymorphicTables = tableMetadata.Count(t => t.IsPolymorphic),
        TotalColumns = totalColumns,
        TotalConstraints = totalConstraints
      };

      // Build complete schema metadata
      string assemblyVersion = typeof(SchemaMetadataGenerator).Assembly
          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
          ?? typeof(SchemaMetadataGenerator).Assembly.GetName().Version?.ToString()
          ?? "0.0.0";

      var schemaMetadata = new SchemaMetadata
      {
        Version = assemblyVersion,
        GeneratedAt = DateTime.UtcNow,
        GeneratedBy = "SchemaMetadataGenerator MSBuild Task",
        Database = _config.Database,
        DefaultSchema = _config.DefaultSchema,
        SqlServerVersion = _config.SqlServerVersion,
        Tables = tableMetadata,
        Statistics = stats,
        Categories = _config.Categories
      };

      // Ensure output directory exists
      string? outputDir = Path.GetDirectoryName(OutputFile);
      if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      // Serialise to JSON
      var options = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      };

      string json = JsonSerializer.Serialize(schemaMetadata, options);
      File.WriteAllText(OutputFile, json, System.Text.Encoding.UTF8);

      // Log summary
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "  Generation Summary");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Total tables:          {stats.TotalTables}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Temporal tables:       {stats.TemporalTables}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Soft delete tables:    {stats.SoftDeleteTables}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Append-only tables:    {stats.AppendOnlyTables}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Polymorphic tables:    {stats.PolymorphicTables}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Total columns:         {stats.TotalColumns}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Total constraints:     {stats.TotalConstraints}");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"+ Metadata written to: {OutputFile}");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Failed to generate schema metadata: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private void LoadConfiguration()
  {
    // Priority: TestConfig > ConfigFile > Fallback defaults
    if (TestConfig != null)
    {
      _config = TestConfig;
      Log.LogMessage("Using injected test configuration");
      return;
    }

    if (!string.IsNullOrEmpty(ConfigFile) && File.Exists(ConfigFile))
    {
      string json = File.ReadAllText(ConfigFile);
      SchemaToolsConfig? deserializedConfig = JsonSerializer.Deserialize<SchemaToolsConfig>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      _config = deserializedConfig ?? new SchemaToolsConfig();

      Log.LogMessage($"Loaded configuration from: {ConfigFile}");
    }
    else
    {
      // Use fallback defaults when no config file
      _config = new SchemaToolsConfig
      {
        Database = DatabaseName,
        DefaultSchema = DefaultSchema,
        SqlServerVersion = SqlServerVersion
      };

      if (!string.IsNullOrEmpty(ConfigFile))
      {
        Log.LogWarning($"Configuration file not found: {ConfigFile}, using defaults");
      }
    }
  }

  private List<string> ResolveSqlFiles()
  {
    // Explicit file list takes precedence
    if (TableFiles != null && TableFiles.Length > 0)
    {
      var files = TableFiles
          .Select(item => item.GetMetadata("FullPath"))
          .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
          .OrderBy(f => f)
          .ToList();

      if (files.Count == 0)
      {
        Log.LogError("TableFiles specified but none of the paths exist");
      }

      return files;
    }

    // Fallback: scan directory
    if (string.IsNullOrEmpty(TablesDirectory) || !Directory.Exists(TablesDirectory))
    {
      Log.LogError(string.IsNullOrEmpty(TablesDirectory)
          ? "No TableFiles or TablesDirectory specified"
          : $"Tables directory not found: {TablesDirectory}");
      return new List<string>();
    }

    var sqlFiles = Directory.GetFiles(TablesDirectory, "*.sql", SearchOption.TopDirectoryOnly)
        .OrderBy(f => f)
        .ToList();

    if (sqlFiles.Count == 0)
    {
      Log.LogError($"No SQL files found in {TablesDirectory}");
    }

    return sqlFiles;
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
      _ => throw new ArgumentException($"Unsupported SQL Server version: {version}")
    };
  }

  private TableMetadata? ParseTableFile(string filePath, TSqlParser parser)
  {
    string sqlText = File.ReadAllText(filePath);
    string fileName = Path.GetFileNameWithoutExtension(filePath);

    // Parse SQL
    using var reader = new StringReader(sqlText);
    TSqlFragment fragment = parser.Parse(reader, out IList<ParseError>? errors);

    // Log parse errors
    if (errors != null && errors.Count > 0)
    {
      foreach (ParseError? error in errors)
      {
        Log.LogWarning($"  Line {error.Line}, Col {error.Column}: {error.Message}");
      }
    }

    // Visit AST
    var visitor = new TableMetadataVisitor();
    fragment.Accept(visitor);

    if (string.IsNullOrEmpty(visitor.TableName))
    {
      Log.LogWarning($"  Could not extract table name from {fileName}");
      return null;
    }

    // Extract metadata from comments
    string? description = SqlCommentParser.ExtractDescription(sqlText);
    string? category = SqlCommentParser.ExtractCategory(sqlText);

    // Resolve per-table overrides
    SchemaToolsConfig effective = _config.ResolveForTable(visitor.TableName!, category);

    // Build table metadata
    var metadata = new TableMetadata
    {
      Name = visitor.TableName!,
      Schema = visitor.SchemaName ?? effective.DefaultSchema,
      Description = description,
      Category = category,
      HasTemporalVersioning = visitor.HasTemporalVersioning,
      HistoryTable = visitor.HistoryTableName != null
            ? $"[{visitor.HistorySchemaName ?? effective.DefaultSchema}].[{visitor.HistoryTableName}]"
            : null
    };

    // Process columns
    ProcessColumns(visitor, metadata, effective);

    // Process constraints (table-level)
    ProcessConstraints(visitor, metadata);

    // Process inline column check constraints (for polymorphic type extraction)
    ExtractInlineCheckConstraints(visitor, metadata);

    // Process indexes
    ProcessIndexes(visitor, metadata);

    // Detect patterns
    DetectTablePatterns(metadata, effective);

    return metadata;
  }

  private void ProcessColumns(TableMetadataVisitor visitor, TableMetadata metadata, SchemaToolsConfig effective)
  {
    foreach (ColumnDefinition colDef in visitor.ColumnDefinitions)
    {
      var column = new ColumnMetadata
      {
        Name = colDef.ColumnIdentifier.Value,
        Type = DataTypeFormatter.Format(colDef.DataType),
        Nullable = true // Default, updated below
      };

      // Process column constraints
      foreach (ConstraintDefinition? constraint in colDef.Constraints)
      {
        switch (constraint)
        {
          case NullableConstraintDefinition nullable:
            // In ScriptDom 170, Nullable is bool, not bool?
            column.Nullable = nullable.Nullable;
            break;

          case DefaultConstraintDefinition defaultConstraint:
            column.DefaultValue = ScriptFragmentFormatter.ToSql(defaultConstraint.Expression);
            column.DefaultConstraintName = defaultConstraint.ConstraintIdentifier?.Value;
            break;

          case UniqueConstraintDefinition unique:
            if (unique.IsPrimaryKey)
            {
              column.IsPrimaryKey = true;
              metadata.PrimaryKey = column.Name;
              metadata.PrimaryKeyType = column.Type;
            }
            else
            {
              column.IsUnique = true;
            }
            break;

          case CheckConstraintDefinition check:
            column.CheckConstraint = ScriptFragmentFormatter.ToSql(check.CheckCondition);
            break;
        }
      }

      // Check for default value via dedicated property (unnamed defaults)
      if (column.DefaultValue == null && colDef.DefaultConstraint != null)
      {
        column.DefaultValue = ScriptFragmentFormatter.ToSql(colDef.DefaultConstraint.Expression);
        column.DefaultConstraintName ??= colDef.DefaultConstraint.ConstraintIdentifier?.Value;
      }

      // Check for identity
      if (colDef.IdentityOptions != null)
      {
        column.IsIdentity = true;
      }

      // Check for computed column
      if (colDef.ComputedColumnExpression != null)
      {
        column.IsComputed = true;
        column.ComputedExpression = ScriptFragmentFormatter.ToSql(colDef.ComputedColumnExpression);
        // In ScriptDom 170, IsPersisted is bool, not bool?
        column.IsPersisted = colDef.IsPersisted;
      }

      // Check for generated always (temporal columns)
      if (colDef.GeneratedAlways != null)
      {
        column.IsGeneratedAlways = true;
        column.GeneratedAlwaysType = colDef.GeneratedAlways.ToString();
      }

      // Detect special columns
      DetectSpecialColumn(column, metadata, effective);

      metadata.Columns.Add(column);
    }
  }

  private static void DetectSpecialColumn(ColumnMetadata column, TableMetadata metadata, SchemaToolsConfig effective)
  {
    string name = column.Name;
    ColumnNamingConfig cols = effective.Columns;

    // Fallback PK detection for columns named "id"
    if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase) &&
        !column.IsPrimaryKey && metadata.PrimaryKey == null)
    {
      metadata.PrimaryKey = column.Name;
      metadata.PrimaryKeyType = column.Type;
      return;
    }

    // Soft-delete active column
    if (string.Equals(name, cols.Active, StringComparison.OrdinalIgnoreCase))
    {
      metadata.HasActiveColumn = true;
      return;
    }

    // Audit columns (created_by / updated_by) -> auto-wire FK
    if (string.Equals(name, cols.CreatedBy, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, cols.UpdatedBy, StringComparison.OrdinalIgnoreCase))
    {
      column.ForeignKey = new ForeignKeyReference
      {
        Table = cols.AuditForeignKeyTable,
        Column = "id",
        Schema = effective.DefaultSchema
      };
      return;
    }

    // Polymorphic patterns (type/id column pairs)
    if (effective.Features.DetectPolymorphicPatterns)
    {
      foreach (PolymorphicPatternConfig pattern in cols.PolymorphicPatterns)
      {
        if (string.Equals(name, pattern.TypeColumn, StringComparison.OrdinalIgnoreCase))
        {
          metadata.IsPolymorphic = true;
          metadata.PolymorphicOwner ??= new PolymorphicOwnerInfo
          {
            TypeColumn = column.Name
          };
          return;
        }

        if (string.Equals(name, pattern.IdColumn, StringComparison.OrdinalIgnoreCase))
        {
          column.IsPolymorphicForeignKey = true;
          metadata.PolymorphicOwner ??= new PolymorphicOwnerInfo();
          metadata.PolymorphicOwner.IdColumn = column.Name;
          return;
        }
      }
    }
  }

  private void ExtractInlineCheckConstraints(TableMetadataVisitor visitor, TableMetadata metadata)
  {
    // Inline check constraints on columns are not in visitor.Constraints,
    // they are in each ColumnDefinition.Constraints. We need to process them
    // after ProcessColumns so that DetectSpecialColumn has already set
    // IsPolymorphic and PolymorphicOwner.
    foreach (ColumnDefinition colDef in visitor.ColumnDefinitions)
    {
      foreach (ConstraintDefinition? constraint in colDef.Constraints)
      {
        if (constraint is CheckConstraintDefinition check)
        {
          ProcessCheckConstraint(check, metadata);
        }
      }
    }
  }

  private void ProcessConstraints(TableMetadataVisitor visitor, TableMetadata metadata)
  {
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      switch (constraint)
      {
        case UniqueConstraintDefinition unique:
          ProcessUniqueConstraint(unique, metadata);
          break;

        case ForeignKeyConstraintDefinition fk:
          ProcessForeignKeyConstraint(fk, metadata);
          break;

        case CheckConstraintDefinition check:
          ProcessCheckConstraint(check, metadata);
          break;
      }
    }
  }

  private static string GetColumnName(ColumnReferenceExpression column)
  {
    // ScriptDom API: MultiPartIdentifier contains the Identifiers list
    MultiPartIdentifier multiPartId = column.MultiPartIdentifier;
    IList<Identifier> identifiers = multiPartId.Identifiers;
    int lastIndex = identifiers.Count - 1;
    Identifier lastIdentifier = identifiers[lastIndex];
    return lastIdentifier.Value;
  }

  private void ProcessUniqueConstraint(UniqueConstraintDefinition unique, TableMetadata metadata)
  {
    var columns = new List<string>();
    foreach (ColumnWithSortOrder? columnWithSort in unique.Columns)
    {
      string columnName = GetColumnName(columnWithSort.Column);
      columns.Add(columnName);
    }

    if (unique.IsPrimaryKey)
    {
      metadata.Constraints.PrimaryKey = new PrimaryKeyConstraint
      {
        Name = unique.ConstraintIdentifier?.Value ?? $"PK_{metadata.Name}",
        Columns = columns,
        IsClustered = unique.Clustered == true
      };

      if (columns.Count == 1 && metadata.PrimaryKey == null)
      {
        metadata.PrimaryKey = columns[0];
        ColumnMetadata? pkColumn = metadata.Columns.FirstOrDefault(c => c.Name == columns[0]);
        if (pkColumn != null)
        {
          metadata.PrimaryKeyType = pkColumn.Type;
          pkColumn.IsPrimaryKey = true;
        }
      }
    }
    else
    {
      metadata.Constraints.UniqueConstraints.Add(new UniqueConstraint
      {
        Name = unique.ConstraintIdentifier?.Value ?? $"UQ_{metadata.Name}_{string.Join("_", columns)}",
        Columns = columns,
        IsClustered = unique.Clustered == true
      });
    }
  }

  private void ProcessForeignKeyConstraint(ForeignKeyConstraintDefinition fk, TableMetadata metadata)
  {
    var columns = new List<string>();
    foreach (Identifier? identifier in fk.Columns)
    {
      columns.Add(identifier.Value);
    }

    var refColumns = new List<string>();
    foreach (Identifier? identifier in fk.ReferencedTableColumns)
    {
      refColumns.Add(identifier.Value);
    }

    var fkConstraint = new ForeignKeyConstraint
    {
      Name = fk.ConstraintIdentifier?.Value ?? $"FK_{metadata.Name}_{fk.ReferenceTableName.BaseIdentifier.Value}",
      Columns = columns,
      ReferencedTable = fk.ReferenceTableName.BaseIdentifier.Value,
      ReferencedSchema = fk.ReferenceTableName.SchemaIdentifier?.Value,
      ReferencedColumns = refColumns,
      OnDelete = fk.DeleteAction.ToString(),
      OnUpdate = fk.UpdateAction.ToString()
    };

    metadata.Constraints.ForeignKeys.Add(fkConstraint);

    // Mark column as having FK
    if (columns.Count == 1)
    {
      string firstColumnName = columns[0];
      ColumnMetadata? column = metadata.Columns.FirstOrDefault(c => c.Name == firstColumnName);
      if (column != null && column.ForeignKey == null)
      {
        column.ForeignKey = new ForeignKeyReference
        {
          Table = fkConstraint.ReferencedTable,
          Column = refColumns[0],
          Schema = fkConstraint.ReferencedSchema
        };
      }
    }
    else
    {
      // Composite FK
      foreach (string col in columns)
      {
        ColumnMetadata? column = metadata.Columns.FirstOrDefault(c => c.Name == col);
        column?.IsCompositeFK = true;
      }
    }
  }

  private void ProcessCheckConstraint(CheckConstraintDefinition check, TableMetadata metadata)
  {
    string expression = ScriptFragmentFormatter.ToSql(check.CheckCondition);

    metadata.Constraints.CheckConstraints.Add(new CheckConstraint
    {
      Name = check.ConstraintIdentifier?.Value ?? $"CK_{metadata.Name}",
      Expression = expression
    });

    // Extract polymorphic allowed types directly from the AST
    if (_config.Features.DetectPolymorphicPatterns &&
        metadata.IsPolymorphic &&
        metadata.PolymorphicOwner != null &&
        check.CheckCondition is InPredicate inPredicate)
    {
      // Verify the IN predicate references the polymorphic type column
      string typeColumn = metadata.PolymorphicOwner.TypeColumn;
      string? referencedColumn = (inPredicate.Expression as ColumnReferenceExpression)
          ?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;

      if (!string.IsNullOrEmpty(typeColumn) &&
          string.Equals(referencedColumn, typeColumn, StringComparison.OrdinalIgnoreCase))
      {
        var types = new List<string>();
        foreach (ScalarExpression? value in inPredicate.Values)
        {
          if (value is StringLiteral literal)
          {
            types.Add(literal.Value);
          }
        }
        if (types.Count > 0)
        {
          metadata.PolymorphicOwner.AllowedTypes = types;
        }
      }
    }
  }

  private void ProcessIndexes(TableMetadataVisitor visitor, TableMetadata metadata)
  {
    foreach (IndexDefinition indexDef in visitor.Indexes)
    {
      var indexColumns = new List<IndexColumn>();
      foreach (ColumnWithSortOrder? columnWithSort in indexDef.Columns)
      {
        var indexColumn = new IndexColumn
        {
          Name = GetColumnName(columnWithSort.Column),
          IsDescending = columnWithSort.SortOrder == SortOrder.Descending
        };
        indexColumns.Add(indexColumn);
      }

      var index = new IndexMetadata
      {
        Name = indexDef.Name?.Value ?? "Unknown",
        IsUnique = indexDef.Unique,
        IsClustered = indexDef.IndexType?.IndexTypeKind == IndexTypeKind.Clustered,
        Columns = indexColumns
      };

      if (indexDef.IncludeColumns != null && indexDef.IncludeColumns.Count > 0)
      {
        var includedCols = new List<string>();
        foreach (ColumnReferenceExpression? columnRef in indexDef.IncludeColumns)
        {
          MultiPartIdentifier multiPartId = columnRef.MultiPartIdentifier;
          IList<Identifier> identifiers = multiPartId.Identifiers;
          int lastIndex = identifiers.Count - 1;
          string columnName = identifiers[lastIndex].Value;
          includedCols.Add(columnName);
        }
        index.IncludedColumns = includedCols;
      }

      if (indexDef.FilterPredicate != null)
      {
        index.FilterClause = $"WHERE {indexDef.FilterPredicate}";
      }

      metadata.Indexes.Add(index);
    }
  }

  private static void DetectTablePatterns(TableMetadata metadata, SchemaToolsConfig effective)
  {
    ColumnNamingConfig cols = effective.Columns;

    // Set ActiveColumnName if the table has an active column
    if (metadata.HasActiveColumn)
    {
      metadata.ActiveColumnName = cols.Active;
    }

    // Detect soft delete ONLY if enabled in config
    if (effective.Features.EnableSoftDelete &&
        metadata.HasActiveColumn &&
        metadata.HasTemporalVersioning)
    {
      metadata.HasSoftDelete = true;
    }

    // Detect append-only ONLY if enabled in config
    if (effective.Features.DetectAppendOnlyTables)
    {
      bool hasCreatedAt = metadata.Columns.Any(c =>
          string.Equals(c.Name, cols.CreatedAt, StringComparison.OrdinalIgnoreCase));
      bool hasUpdatedBy = metadata.Columns.Any(c =>
          string.Equals(c.Name, cols.UpdatedBy, StringComparison.OrdinalIgnoreCase));

      if (hasCreatedAt && !hasUpdatedBy && !metadata.HasTemporalVersioning)
      {
        metadata.IsAppendOnly = true;
      }
    }
  }
}
