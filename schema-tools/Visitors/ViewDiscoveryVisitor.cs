using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Visitors;

/// <summary>
/// Visits T-SQL AST to discover existing view definitions.
/// Used for explicit-wins policy: user-defined views take precedence over generated ones.
/// </summary>
public class ViewDiscoveryVisitor : TSqlFragmentVisitor
{
  /// <summary>
  /// List of views discovered in the parsed SQL.
  /// </summary>
  public List<DiscoveredView> Views { get; } = [];

  public override void Visit(CreateViewStatement node)
  {
    string? schema = node.SchemaObjectName?.SchemaIdentifier?.Value;
    string name = node.SchemaObjectName?.BaseIdentifier?.Value ?? string.Empty;

    Views.Add(new DiscoveredView
    {
      Name = name,
      Schema = schema
    });

    base.Visit(node);
  }

  public override void Visit(CreateOrAlterViewStatement node)
  {
    string? schema = node.SchemaObjectName?.SchemaIdentifier?.Value;
    string name = node.SchemaObjectName?.BaseIdentifier?.Value ?? string.Empty;

    Views.Add(new DiscoveredView
    {
      Name = name,
      Schema = schema
    });

    base.Visit(node);
  }

  public override void Visit(AlterViewStatement node)
  {
    // Also track ALTER VIEW as it indicates explicit ownership
    string? schema = node.SchemaObjectName?.SchemaIdentifier?.Value;
    string name = node.SchemaObjectName?.BaseIdentifier?.Value ?? string.Empty;

    Views.Add(new DiscoveredView
    {
      Name = name,
      Schema = schema
    });

    base.Visit(node);
  }
}

/// <summary>
/// Represents a view discovered during source scanning.
/// </summary>
public class DiscoveredView
{
  public string Name { get; set; } = string.Empty;
  public string? Schema { get; set; }
}
