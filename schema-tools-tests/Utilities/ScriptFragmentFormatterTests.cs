using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class ScriptFragmentFormatterTests
{
  private static TSqlFragment ParseExpression(string expressionSql)
  {
    // Parse a SELECT to extract an expression from ScriptDom
    string sql = $"SELECT {expressionSql};";
    var parser = new TSql170Parser(false);
    TSqlFragment fragment = parser.Parse(new StringReader(sql), out IList<ParseError>? errors);

    errors.Should().BeEmpty("SQL should parse without errors: {0}", expressionSql);

    var script = (TSqlScript)fragment;
    var select = (SelectStatement)script.Batches[0].Statements[0];
    var querySpec = (QuerySpecification)select.QueryExpression;
    var selectElement = (SelectScalarExpression)querySpec.SelectElements[0];
    return selectElement.Expression;
  }

  [Fact]
  public void ToSql_NullFragment_ReturnsEmptyString()
  {
    ScriptFragmentFormatter.ToSql(null).Should().BeEmpty();
  }

  [Fact]
  public void ToSql_FunctionCall_ReturnsSqlText()
  {
    TSqlFragment fragment = ParseExpression("SYSUTCDATETIME()");
    string sql = ScriptFragmentFormatter.ToSql(fragment);

    sql.Should().ContainEquivalentOf("SYSUTCDATETIME");
  }

  [Fact]
  public void ToSql_InPredicate_ReturnsSqlText()
  {
    string sql = "CREATE TABLE x (t VARCHAR(20) CHECK (t IN ('a', 'b')));";
    var parser = new TSql170Parser(false);
    TSqlFragment fragment = parser.Parse(new StringReader(sql), out IList<ParseError>? errors);
    errors.Should().BeEmpty();

    var create = (CreateTableStatement)((TSqlScript)fragment).Batches[0].Statements[0];
    var check = (CheckConstraintDefinition)create.Definition.ColumnDefinitions[0].Constraints[0];
    string result = ScriptFragmentFormatter.ToSql(check.CheckCondition);

    result.Should().Contain("IN");
    result.Should().Contain("'a'");
    result.Should().Contain("'b'");
  }

  [Fact]
  public void ToSql_StringLiteral_ReturnsSqlText()
  {
    TSqlFragment fragment = ParseExpression("'hello world'");
    string sql = ScriptFragmentFormatter.ToSql(fragment);

    sql.Should().Contain("hello world");
  }
}
