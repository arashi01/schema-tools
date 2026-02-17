using SchemaTools.Models;

namespace SchemaTools.Tests.Fixtures.Builders;

/// <summary>
/// Fluent builder for <see cref="TableMetadata"/> test instances.
/// Uses <c>with</c> expressions to preserve record immutability.
/// </summary>
internal sealed class TableMetadataBuilder
{
  private TableMetadata _table;

  public TableMetadataBuilder(string name)
  {
    _table = new TableMetadata
    {
      Name = name,
      Schema = "test",
      PrimaryKey = "id",
      Columns =
      [
        new ColumnMetadata { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
        new ColumnMetadata { Name = "record_created_by", Type = "UNIQUEIDENTIFIER" },
        new ColumnMetadata { Name = "record_updated_by", Type = "UNIQUEIDENTIFIER" }
      ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = $"pk_{name}", Columns = ["id"] }
      }
    };
  }

  public TableMetadataBuilder WithSchema(string schema)
  {
    _table = _table with { Schema = schema };
    return this;
  }

  public TableMetadataBuilder WithCategory(string category)
  {
    _table = _table with { Category = category };
    return this;
  }

  public TableMetadataBuilder WithDescription(string description)
  {
    _table = _table with { Description = description };
    return this;
  }

  public TableMetadataBuilder WithSoftDelete(string activeColumnName = "record_active")
  {
    _table = _table with { HasSoftDelete = true, HasActiveColumn = true, ActiveColumnName = activeColumnName };
    return this;
  }

  public TableMetadataBuilder WithTemporalVersioning(string? historyTable = null)
  {
    _table = _table with { HasTemporalVersioning = true, HistoryTable = historyTable ?? $"{_table.Name}_history" };
    return this;
  }

  public TableMetadataBuilder AsAppendOnly()
  {
    _table = _table with { IsAppendOnly = true };
    return this;
  }

  public TableMetadataBuilder AsPolymorphic(string typeColumn = "entity_type", string idColumn = "entity_id",
    params string[] allowedTypes)
  {
    _table = _table with
    {
      IsPolymorphic = true,
      PolymorphicOwner = new PolymorphicOwnerInfo
      {
        TypeColumn = typeColumn,
        IdColumn = idColumn,
        AllowedTypes = [.. allowedTypes]
      }
    };
    return this;
  }

  public TableMetadataBuilder AsHistoryTable()
  {
    _table = _table with { IsHistoryTable = true, PrimaryKey = null };
    return this;
  }

  public TableMetadataBuilder WithPrimaryKey(string column, string type = "UNIQUEIDENTIFIER")
  {
    _table = _table with { PrimaryKey = column, PrimaryKeyType = type };
    return this;
  }

  public TableMetadataBuilder WithColumn(ColumnMetadata column)
  {
    _table = _table with { Columns = [.. _table.Columns, column] };
    return this;
  }

  public TableMetadataBuilder WithColumn(string name, string type, bool nullable = false,
    Func<ColumnMetadata, ColumnMetadata>? configure = null)
  {
    ColumnMetadata column = new() { Name = name, Type = type, Nullable = nullable };
    if (configure != null)
    {
      column = configure(column);
    }
    _table = _table with { Columns = [.. _table.Columns, column] };
    return this;
  }

  public TableMetadataBuilder WithForeignKey(string columnName, string referencedTable,
    string referencedColumn = "id", ForeignKeyAction onDelete = ForeignKeyAction.NoAction, ForeignKeyAction onUpdate = ForeignKeyAction.NoAction)
  {
    ColumnMetadata fkColumn = new()
    {
      Name = columnName,
      Type = "UNIQUEIDENTIFIER",
      ForeignKey = new ForeignKeyReference
      {
        Table = referencedTable,
        Column = referencedColumn,
        Schema = _table.Schema
      }
    };

    ForeignKeyConstraint fk = new()
    {
      Name = $"fk_{_table.Name}_{columnName}",
      Columns = [columnName],
      ReferencedTable = referencedTable,
      ReferencedSchema = _table.Schema,
      ReferencedColumns = [referencedColumn],
      OnDelete = onDelete,
      OnUpdate = onUpdate
    };

    _table = _table with
    {
      Columns = [.. _table.Columns, fkColumn],
      Constraints = _table.Constraints with { ForeignKeys = [.. _table.Constraints.ForeignKeys, fk] }
    };

    return this;
  }

  public TableMetadataBuilder WithConstraints(Func<ConstraintsCollection, ConstraintsCollection> configure)
  {
    _table = _table with { Constraints = configure(_table.Constraints) };
    return this;
  }

  public TableMetadataBuilder WithColumns(params ColumnMetadata[] columns)
  {
    _table = _table with { Columns = [.. columns] };
    return this;
  }

  public TableMetadataBuilder Configure(Func<TableMetadata, TableMetadata> configure)
  {
    _table = configure(_table);
    return this;
  }

  public TableMetadata Build() => _table;
}
