using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Visitors;

/// <summary>
/// Visits T-SQL AST to extract table metadata
/// </summary>
public class TableMetadataVisitor : TSqlFragmentVisitor
{
  public string? SchemaName { get; private set; }
  public string? TableName { get; private set; }
  public List<ColumnDefinition> ColumnDefinitions { get; } = new();
  public List<ConstraintDefinition> Constraints { get; } = new();
  public List<IndexDefinition> Indexes { get; } = new();
  public bool HasTemporalVersioning { get; private set; }
  public string? HistoryTableName { get; private set; }
  public string? HistorySchemaName { get; private set; }

  public override void Visit(CreateTableStatement node)
  {
    if (node.SchemaObjectName.SchemaIdentifier != null)
    {
      SchemaName = node.SchemaObjectName.SchemaIdentifier.Value;
    }
    TableName = node.SchemaObjectName.BaseIdentifier.Value;

    foreach (ColumnDefinition? element in node.Definition.ColumnDefinitions)
    {
      ColumnDefinitions.Add(element);
    }

    foreach (ConstraintDefinition? constraint in node.Definition.TableConstraints)
    {
      Constraints.Add(constraint);
    }

    // Extract indexes - check if they exist on the definition
    // In SQL Server 2016+, indexes can be defined inline with CREATE TABLE
    // The API changed in newer versions of ScriptDom
    ExtractIndexes(node);

    // Check for temporal versioning - SQL Server 2016+
    ExtractTemporalVersioning(node);

    base.Visit(node);
  }

  public override void Visit(AlterTableAddTableElementStatement node)
  {
    // Handles the common pattern where PKs and FKs are defined after CREATE TABLE
    if (node.Definition?.TableConstraints != null)
    {
      foreach (ConstraintDefinition constraint in node.Definition.TableConstraints)
      {
        Constraints.Add(constraint);
      }
    }

    base.Visit(node);
  }

  private void ExtractIndexes(CreateTableStatement node)
  {
    // Try to extract indexes using reflection to handle API differences
    // between ScriptDom versions
    try
    {
      Type definitionType = node.Definition.GetType();

      // Try IndexDefinitions property (older ScriptDom versions)
      PropertyInfo? indexDefsProperty = definitionType.GetProperty("IndexDefinitions");
      if (indexDefsProperty != null)
      {
        if (indexDefsProperty.GetValue(node.Definition) is IList<IndexDefinition> indexDefs)
        {
          foreach (IndexDefinition index in indexDefs)
          {
            Indexes.Add(index);
          }
        }
        return;
      }

      // Try Indexes property on the table statement itself (newer versions)
      Type tableType = node.GetType();
      PropertyInfo? indexesProperty = tableType.GetProperty("Indexes");
      if (indexesProperty != null)
      {
        if (indexesProperty.GetValue(node) is IList<IndexDefinition> indexes)
        {
          foreach (IndexDefinition index in indexes)
          {
            Indexes.Add(index);
          }
        }
      }
    }
    catch
    {
      // Silently ignore if neither property exists
      // Indexes will just be empty
    }
  }

  private void ExtractTemporalVersioning(CreateTableStatement node)
  {
    // In ScriptDom 170, temporal/system-versioning options are in
    // CreateTableStatement.Options (IList<TableOption>)
    if (node.Options != null && node.Options.Count > 0)
    {
      ProcessTemporalOptions(node.Options);
    }
  }

  private void ProcessTemporalOptions(IList<TableOption> options)
  {
    foreach (TableOption option in options)
    {
      if (option is SystemVersioningTableOption temporalOption)
      {
        HasTemporalVersioning = true;

        // Try to extract history table name using reflection
        // Property name changed between ScriptDom versions
        try
        {
          Type optionType = temporalOption.GetType();

          // Try HistoryTable property (ScriptDom 170+)
          PropertyInfo? historyProperty = optionType.GetProperty("HistoryTable") ?? optionType.GetProperty("HistoryTableName");
          if (historyProperty != null)
          {
            if (historyProperty.GetValue(temporalOption) is SchemaObjectName historyTableValue)
            {
              HistorySchemaName = historyTableValue.SchemaIdentifier?.Value;
              HistoryTableName = historyTableValue.BaseIdentifier.Value;
            }
          }
        }
        catch
        {
          // History table name extraction failed, but we still know it's temporal
        }
      }
    }
  }
}
