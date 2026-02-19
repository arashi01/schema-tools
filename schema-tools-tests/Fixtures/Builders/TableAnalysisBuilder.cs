using SchemaTools.Models;

namespace SchemaTools.Tests.Fixtures.Builders;

/// <summary>
/// Fluent builder for <see cref="TableAnalysis"/> test instances.
/// Uses <c>with</c> expressions to preserve record immutability.
/// </summary>
internal sealed class TableAnalysisBuilder
{
  private TableAnalysis _table;

  public TableAnalysisBuilder(string name)
  {
    _table = new TableAnalysis
    {
      Name = name,
      Schema = "test",
      PrimaryKeyColumns = ["id"]
    };
  }

  public TableAnalysisBuilder WithSchema(string schema)
  {
    _table = _table with { Schema = schema };
    return this;
  }

  public TableAnalysisBuilder WithCategory(string category)
  {
    _table = _table with { Category = category };
    return this;
  }

  public TableAnalysisBuilder WithDescription(string description)
  {
    _table = _table with { Description = description };
    return this;
  }

  public TableAnalysisBuilder WithColumnDescriptions(Dictionary<string, string> columnDescriptions)
  {
    _table = _table with { ColumnDescriptions = columnDescriptions };
    return this;
  }

  public TableAnalysisBuilder WithSourceFile(string sourceFile)
  {
    _table = _table with { SourceFile = sourceFile };
    return this;
  }

  public TableAnalysisBuilder WithSoftDelete(
    SoftDeleteMode mode = SoftDeleteMode.Cascade,
    string activeColumnName = "record_active")
  {
    _table = _table with
    {
      HasSoftDelete = true,
      HasActiveColumn = true,
      ActiveColumnName = activeColumnName,
      SoftDeleteMode = mode
    };
    return this;
  }

  public TableAnalysisBuilder WithTemporalVersioning(
    string? historyTable = null,
    string validFrom = "valid_from",
    string validTo = "valid_to")
  {
    _table = _table with
    {
      HasTemporalVersioning = true,
      HistoryTable = historyTable ?? $"{_table.Name}_history",
      ValidFromColumn = validFrom,
      ValidToColumn = validTo
    };
    return this;
  }

  public TableAnalysisBuilder WithReactivationCascade(int toleranceMs = 5000)
  {
    _table = _table with { ReactivationCascade = true, ReactivationCascadeToleranceMs = toleranceMs };
    return this;
  }

  public TableAnalysisBuilder WithPrimaryKey(params string[] columns)
  {
    _table = _table with { PrimaryKeyColumns = [.. columns] };
    return this;
  }

  public TableAnalysisBuilder WithForeignKeyTo(
    string referencedTable,
    string column = "parent_id",
    string referencedColumn = "id",
    string? referencedSchema = null,
    ForeignKeyAction onDelete = ForeignKeyAction.NoAction)
  {
    _table = _table with
    {
      ForeignKeyReferences =
      [
        .. _table.ForeignKeyReferences,
        new ForeignKeyRef
        {
          ReferencedTable = referencedTable,
          ReferencedSchema = referencedSchema ?? _table.Schema,
          Columns = [column],
          ReferencedColumns = [referencedColumn],
          OnDelete = onDelete
        }
      ]
    };
    return this;
  }

  public TableAnalysisBuilder WithChildTables(params string[] childTableNames)
  {
    _table = _table with { ChildTables = [.. childTableNames], IsLeafTable = false };
    return this;
  }

  public TableAnalysisBuilder AsLeafTable()
  {
    _table = _table with { IsLeafTable = true, ChildTables = [] };
    return this;
  }

  public TableAnalysisBuilder Configure(Func<TableAnalysis, TableAnalysis> configure)
  {
    _table = configure(_table);
    return this;
  }

  public TableAnalysis Build() => _table;
}
