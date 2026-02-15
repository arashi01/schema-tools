using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Visitors;

/// <summary>
/// Visits T-SQL AST to discover existing trigger definitions.
/// Used for explicit-wins policy: user-defined triggers take precedence over generated ones.
/// </summary>
public class TriggerDiscoveryVisitor : TSqlFragmentVisitor
{
  /// <summary>
  /// List of triggers discovered in the parsed SQL
  /// </summary>
  public List<DiscoveredTrigger> Triggers { get; } = [];

  public override void Visit(CreateTriggerStatement node)
  {
    string? schema = node.Name.SchemaIdentifier?.Value;
    string name = node.Name.BaseIdentifier.Value;

    string? targetSchema = node.TriggerObject?.Name?.SchemaIdentifier?.Value;
    string? targetTable = node.TriggerObject?.Name?.BaseIdentifier?.Value;

    Triggers.Add(new DiscoveredTrigger
    {
      Name = name,
      Schema = schema,
      TargetTable = targetTable,
      TargetSchema = targetSchema
    });

    base.Visit(node);
  }

  public override void Visit(CreateOrAlterTriggerStatement node)
  {
    string? schema = node.Name.SchemaIdentifier?.Value;
    string name = node.Name.BaseIdentifier.Value;

    string? targetSchema = node.TriggerObject?.Name?.SchemaIdentifier?.Value;
    string? targetTable = node.TriggerObject?.Name?.BaseIdentifier?.Value;

    Triggers.Add(new DiscoveredTrigger
    {
      Name = name,
      Schema = schema,
      TargetTable = targetTable,
      TargetSchema = targetSchema
    });

    base.Visit(node);
  }

  public override void Visit(AlterTriggerStatement node)
  {
    // Also track ALTER TRIGGER as it indicates explicit ownership
    string? schema = node.Name.SchemaIdentifier?.Value;
    string name = node.Name.BaseIdentifier.Value;

    string? targetSchema = node.TriggerObject?.Name?.SchemaIdentifier?.Value;
    string? targetTable = node.TriggerObject?.Name?.BaseIdentifier?.Value;

    Triggers.Add(new DiscoveredTrigger
    {
      Name = name,
      Schema = schema,
      TargetTable = targetTable,
      TargetSchema = targetSchema
    });

    base.Visit(node);
  }
}

/// <summary>
/// Represents a trigger discovered during source scanning
/// </summary>
public class DiscoveredTrigger
{
  public string Name { get; set; } = string.Empty;
  public string? Schema { get; set; }
  public string? TargetTable { get; set; }
  public string? TargetSchema { get; set; }
}
