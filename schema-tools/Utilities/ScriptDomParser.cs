using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Utilities;

/// <summary>
/// Parses T-SQL scripts via ScriptDom AST, replacing all hand-rolled string parsing.
/// Used by the metadata extractor to extract metadata from DacFx
/// <c>GetScript()</c> output where property access fails due to
/// SqlScriptProperty/InvalidCastException limitations.
/// </summary>
/// <remarks>
/// All methods are stateless and side-effect-free, making them trivially testable.
/// The parser defaults to Sql170 (SQL Server 2025) but accepts any version string
/// recognised by <see cref="ParserFactory"/>
/// </remarks>
public static class ScriptDomParser
{
  /// <summary>
  /// Computed column information extracted from a CREATE TABLE column definition.
  /// </summary>
  public sealed class ComputedColumnInfo
  {
    /// <summary>The expression text (e.g. "([sort_order] * [weight])").</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>Whether the PERSISTED keyword is present.</summary>
    public bool IsPersisted { get; set; }
  }

  /// <summary>
  /// FK referential action information extracted from a constraint definition.
  /// </summary>
  public sealed class ForeignKeyActionInfo
  {
    /// <summary>ON DELETE action in PascalCase (e.g. "Cascade", "NoAction").</summary>
    public string OnDelete { get; set; } = "NoAction";

    /// <summary>ON UPDATE action in PascalCase (e.g. "Cascade", "NoAction").</summary>
    public string OnUpdate { get; set; } = "NoAction";
  }

  // ------------------------------------------------------------------
  //  CHECK constraints
  // ------------------------------------------------------------------

  /// <summary>
  /// Extracts the CHECK expression from a constraint script.
  /// Input: "ALTER TABLE [dbo].[t] ADD CONSTRAINT [ck_x] CHECK ([col] > 0)"
  /// Output: "([col] > 0)"
  /// </summary>
  public static string? ExtractCheckExpression(string? script, string sqlVersion = "Sql170")
  {
    if (string.IsNullOrWhiteSpace(script))
    {
      return null;
    }

    foreach (TSqlStatement stmt in ParseStatements(script!, sqlVersion))
    {
      // ALTER TABLE ... ADD CONSTRAINT ... CHECK (...)
      if (stmt is AlterTableAddTableElementStatement addStmt)
      {
        foreach (ConstraintDefinition constraint in addStmt.Definition.TableConstraints)
        {
          if (constraint is CheckConstraintDefinition check)
          {
            return ScriptFragmentFormatter.ToSql(check.CheckCondition);
          }
        }
      }
    }

    return null;
  }

  /// <summary>
  /// Extracts a CHECK expression from a CREATE TABLE script by constraint name.
  /// Searches the table DDL for named inline CHECK constraints.
  /// </summary>
  public static string? ExtractCheckExpressionByName(string? tableScript, string constraintName, string sqlVersion = "Sql170")
  {
    if (string.IsNullOrWhiteSpace(tableScript) || string.IsNullOrEmpty(constraintName))
    {
      return null;
    }

    foreach (TSqlStatement stmt in ParseStatements(tableScript!, sqlVersion))
    {
      if (stmt is not CreateTableStatement createTable)
      {
        continue;
      }

      foreach (ConstraintDefinition constraint in createTable.Definition.TableConstraints)
      {
        if (constraint is CheckConstraintDefinition check
            && IdentifierEquals(constraint.ConstraintIdentifier, constraintName))
        {
          return ScriptFragmentFormatter.ToSql(check.CheckCondition);
        }
      }

      foreach (ColumnDefinition col in createTable.Definition.ColumnDefinitions)
      {
        foreach (ConstraintDefinition constraint in col.Constraints)
        {
          if (constraint is CheckConstraintDefinition check
              && IdentifierEquals(constraint.ConstraintIdentifier, constraintName))
          {
            return ScriptFragmentFormatter.ToSql(check.CheckCondition);
          }
        }
      }
    }

    return null;
  }

  // ------------------------------------------------------------------
  //  DEFAULT constraints
  // ------------------------------------------------------------------

  /// <summary>
  /// Extracts the DEFAULT expression from a constraint script.
  /// Input: "ALTER TABLE [dbo].[t] ADD CONSTRAINT [df_x] DEFAULT ((0)) FOR [col]"
  /// Output: "((0))"
  /// </summary>
  public static string? ExtractDefaultExpression(string? script, string sqlVersion = "Sql170")
  {
    if (string.IsNullOrWhiteSpace(script))
    {
      return null;
    }

    foreach (TSqlStatement stmt in ParseStatements(script!, sqlVersion))
    {
      if (stmt is AlterTableAddTableElementStatement addStmt)
      {
        // ALTER TABLE ... ADD DEFAULT (expr) FOR [col]
        // or ALTER TABLE ... ADD CONSTRAINT [name] DEFAULT (expr) FOR [col]
        foreach (ConstraintDefinition constraint in addStmt.Definition.TableConstraints)
        {
          if (constraint is DefaultConstraintDefinition def)
          {
            return ScriptFragmentFormatter.ToSql(def.Expression);
          }
        }

        // Also check column definitions with inline defaults
        foreach (ColumnDefinition col in addStmt.Definition.ColumnDefinitions)
        {
          if (col.DefaultConstraint != null)
          {
            return ScriptFragmentFormatter.ToSql(col.DefaultConstraint.Expression);
          }
        }
      }
    }

    return null;
  }

  // ------------------------------------------------------------------
  //  Computed columns
  // ------------------------------------------------------------------

  /// <summary>
  /// Extracts computed column info from a CREATE TABLE script by column name.
  /// Returns null if the column is not computed or not found.
  /// </summary>
  public static ComputedColumnInfo? ExtractComputedColumn(
    string? tableScript, string columnName, string sqlVersion = "Sql170")
  {
    if (string.IsNullOrWhiteSpace(tableScript) || string.IsNullOrEmpty(columnName))
    {
      return null;
    }

    foreach (TSqlStatement stmt in ParseStatements(tableScript!, sqlVersion))
    {
      if (stmt is not CreateTableStatement createTable)
      {
        continue;
      }

      foreach (ColumnDefinition col in createTable.Definition.ColumnDefinitions)
      {
        if (col.ComputedColumnExpression != null
            && IdentifierEquals(col.ColumnIdentifier, columnName))
        {
          return new ComputedColumnInfo
          {
            Expression = ScriptFragmentFormatter.ToSql(col.ComputedColumnExpression),
            IsPersisted = col.IsPersisted
          };
        }
      }
    }

    return null;
  }

  /// <summary>
  /// Extracts all computed columns from a CREATE TABLE script.
  /// Returns a dictionary of column name -> computed column info.
  /// </summary>
  public static Dictionary<string, ComputedColumnInfo> ExtractAllComputedColumns(
    string? tableScript, string sqlVersion = "Sql170")
  {
    var result = new Dictionary<string, ComputedColumnInfo>(StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(tableScript))
    {
      return result;
    }

    foreach (TSqlStatement stmt in ParseStatements(tableScript!, sqlVersion))
    {
      if (stmt is not CreateTableStatement createTable)
      {
        continue;
      }

      foreach (ColumnDefinition col in createTable.Definition.ColumnDefinitions)
      {
        if (col.ComputedColumnExpression != null && col.ColumnIdentifier?.Value != null)
        {
          result[col.ColumnIdentifier.Value] = new ComputedColumnInfo
          {
            Expression = ScriptFragmentFormatter.ToSql(col.ComputedColumnExpression),
            IsPersisted = col.IsPersisted
          };
        }
      }
    }

    return result;
  }

  // ------------------------------------------------------------------
  //  FK referential actions
  // ------------------------------------------------------------------

  /// <summary>
  /// Extracts FK referential actions (ON DELETE / ON UPDATE) from a script.
  /// Works with ALTER TABLE ADD CONSTRAINT scripts, CREATE TABLE scripts, or FK constraint scripts.
  /// Returns the first FK constraint found.
  /// </summary>
  public static ForeignKeyActionInfo ExtractFKActions(string? script, string sqlVersion = "Sql170")
  {
    var result = new ForeignKeyActionInfo();
    if (string.IsNullOrWhiteSpace(script))
    {
      return result;
    }

    foreach (TSqlStatement stmt in ParseStatements(script!, sqlVersion))
    {
      ForeignKeyConstraintDefinition? fkDef = FindForeignKeyConstraint(stmt);
      if (fkDef != null)
      {
        result.OnDelete = ConvertDeleteUpdateAction(fkDef.DeleteAction);
        result.OnUpdate = ConvertDeleteUpdateAction(fkDef.UpdateAction);
        return result;
      }
    }

    return result;
  }

  /// <summary>
  /// Extracts FK referential actions for a named FK constraint from a CREATE TABLE script.
  /// </summary>
  public static ForeignKeyActionInfo ExtractFKActionsByName(
    string? tableScript, string constraintName, string sqlVersion = "Sql170")
  {
    var result = new ForeignKeyActionInfo();
    if (string.IsNullOrWhiteSpace(tableScript) || string.IsNullOrEmpty(constraintName))
    {
      return result;
    }

    foreach (TSqlStatement stmt in ParseStatements(tableScript!, sqlVersion))
    {
      if (stmt is not CreateTableStatement createTable)
      {
        continue;
      }

      foreach (ConstraintDefinition constraint in createTable.Definition.TableConstraints)
      {
        if (constraint is ForeignKeyConstraintDefinition fk
            && IdentifierEquals(constraint.ConstraintIdentifier, constraintName))
        {
          result.OnDelete = ConvertDeleteUpdateAction(fk.DeleteAction);
          result.OnUpdate = ConvertDeleteUpdateAction(fk.UpdateAction);
          return result;
        }
      }
    }

    return result;
  }

  // ------------------------------------------------------------------
  //  Index filter predicates
  // ------------------------------------------------------------------

  /// <summary>
  /// Extracts the WHERE filter clause from an index creation script.
  /// Input: "CREATE UNIQUE NONCLUSTERED INDEX ... WHERE ([active] = 1)"
  /// Output: "WHERE ([active] = 1)"
  /// </summary>
  public static string? ExtractFilterClause(string? script, string sqlVersion = "Sql170")
  {
    if (string.IsNullOrWhiteSpace(script))
    {
      return null;
    }

    foreach (TSqlStatement stmt in ParseStatements(script!, sqlVersion))
    {
      if (stmt is CreateIndexStatement idx && idx.FilterPredicate != null)
      {
        return $"WHERE {ScriptFragmentFormatter.ToSql(idx.FilterPredicate)}";
      }
    }

    return null;
  }

  // ------------------------------------------------------------------
  //  Polymorphic types extraction
  // ------------------------------------------------------------------

  /// <summary>
  /// Extracts allowed types from CHECK constraint IN-list expressions.
  /// Given expression: "([owner_type] IN ('user', 'organisation'))"
  /// Returns: ["user", "organisation"]
  /// </summary>
  public static List<string> ExtractAllowedTypesFromExpression(string? expression, string sqlVersion = "Sql170")
  {
    var types = new List<string>();
    if (string.IsNullOrWhiteSpace(expression))
    {
      return types;
    }

    // Parse as a boolean expression by wrapping in a SELECT WHERE
    string wrappedSql = $"SELECT 1 WHERE {expression}";
    foreach (TSqlStatement stmt in ParseStatements(wrappedSql, sqlVersion))
    {
      if (stmt is SelectStatement select)
      {
        QuerySpecification? spec = select.QueryExpression as QuerySpecification;
        if (spec?.WhereClause?.SearchCondition != null)
        {
          CollectStringLiterals(spec.WhereClause.SearchCondition, types);
        }
      }
    }

    return types;
  }

  // ------------------------------------------------------------------
  //  Internal helpers
  // ------------------------------------------------------------------

  /// <summary>
  /// Parses a T-SQL script and returns the statements.
  /// Silently returns empty if parsing fails.
  /// </summary>
  internal static IEnumerable<TSqlStatement> ParseStatements(string script, string sqlVersion = "Sql170")
  {
    TSqlParser parser = ParserFactory.CreateParser(sqlVersion);
    using var reader = new StringReader(script);
    TSqlFragment fragment = parser.Parse(reader, out IList<ParseError> errors);

    if (fragment is TSqlScript tsqlScript)
    {
      foreach (TSqlBatch batch in tsqlScript.Batches)
      {
        foreach (TSqlStatement stmt in batch.Statements)
        {
          yield return stmt;
        }
      }
    }
  }

  /// <summary>
  /// Converts a ScriptDom DeleteUpdateAction enum to PascalCase string
  /// matching the format used by DacFx ForeignKeyAction.ToString().
  /// </summary>
  internal static string ConvertDeleteUpdateAction(DeleteUpdateAction action)
  {
    return action switch
    {
      DeleteUpdateAction.Cascade => "Cascade",
      DeleteUpdateAction.SetNull => "SetNull",
      DeleteUpdateAction.SetDefault => "SetDefault",
      _ => "NoAction"
    };
  }

  /// <summary>
  /// Recursively collects string literal values from a boolean expression AST node.
  /// Used to extract allowed types from IN ('value1', 'value2', ...) patterns.
  /// </summary>
  private static void CollectStringLiterals(BooleanExpression expr, List<string> values)
  {
    if (expr is BooleanParenthesisExpression paren)
    {
      CollectStringLiterals(paren.Expression, values);
    }
    else if (expr is InPredicate inPred)
    {
      foreach (ScalarExpression val in inPred.Values)
      {
        if (val is StringLiteral literal)
        {
          values.Add(literal.Value);
        }
      }
    }
    else if (expr is BooleanBinaryExpression binary)
    {
      CollectStringLiterals(binary.FirstExpression, values);
      CollectStringLiterals(binary.SecondExpression, values);
    }
  }

  /// <summary>
  /// Finds the first ForeignKeyConstraintDefinition in a statement.
  /// Handles ALTER TABLE ADD CONSTRAINT and CREATE TABLE statements.
  /// </summary>
  private static ForeignKeyConstraintDefinition? FindForeignKeyConstraint(TSqlStatement stmt)
  {
    if (stmt is AlterTableAddTableElementStatement addStmt)
    {
      foreach (ConstraintDefinition constraint in addStmt.Definition.TableConstraints)
      {
        if (constraint is ForeignKeyConstraintDefinition fk)
        {
          return fk;
        }
      }
    }

    if (stmt is CreateTableStatement createTable)
    {
      foreach (ConstraintDefinition constraint in createTable.Definition.TableConstraints)
      {
        if (constraint is ForeignKeyConstraintDefinition fk)
        {
          return fk;
        }
      }
    }

    return null;
  }

  /// <summary>
  /// Compares an AST identifier against a name string (case-insensitive).
  /// Handles both bracketed and unbracketed identifiers.
  /// </summary>
  private static bool IdentifierEquals(Identifier? identifier, string name)
  {
    if (identifier == null || string.IsNullOrEmpty(name))
    {
      return false;
    }

    return string.Equals(identifier.Value, name, StringComparison.OrdinalIgnoreCase);
  }
}
