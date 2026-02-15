using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class DataTypeFormatterTests
{
  private static DataTypeReference ParseDataType(string columnTypeSql)
  {
    // Parse a minimal CREATE TABLE to extract the data type from ScriptDom
    string sql = $"CREATE TABLE x ({columnTypeSql} NULL);";
    var parser = new TSql170Parser(false);
    TSqlFragment fragment = parser.Parse(new StringReader(sql), out IList<ParseError>? errors);

    errors.Should().BeEmpty("SQL should parse without errors: {0}", columnTypeSql);

    CreateTableStatement? create = fragment.ScriptTokenStream != null
        ? ((TSqlScript)fragment).Batches[0].Statements[0] as CreateTableStatement
        : null;

    create.Should().NotBeNull();
    return create!.Definition.ColumnDefinitions[0].DataType;
  }

  [Theory]
  [InlineData("col INT", "INT")]
  [InlineData("col BIGINT", "BIGINT")]
  [InlineData("col BIT", "BIT")]
  [InlineData("col UNIQUEIDENTIFIER", "UNIQUEIDENTIFIER")]
  [InlineData("col DATETIME2", "DATETIME2")]
  [InlineData("col MONEY", "MONEY")]
  public void Format_SimpleTypes_ReturnsUppercaseName(string colSql, string expected)
  {
    DataTypeReference dataType = ParseDataType(colSql);
    DataTypeFormatter.Format(dataType).Should().Be(expected);
  }

  [Theory]
  [InlineData("col VARCHAR(100)", "VARCHAR(100)")]
  [InlineData("col NVARCHAR(50)", "NVARCHAR(50)")]
  [InlineData("col DECIMAL(18, 2)", "DECIMAL(18, 2)")]
  [InlineData("col NUMERIC(10, 4)", "NUMERIC(10, 4)")]
  [InlineData("col DATETIME2(7)", "DATETIME2(7)")]
  [InlineData("col DATETIMEOFFSET(7)", "DATETIMEOFFSET(7)")]
  public void Format_ParameterisedTypes_IncludesParameters(string colSql, string expected)
  {
    DataTypeReference dataType = ParseDataType(colSql);
    DataTypeFormatter.Format(dataType).Should().Be(expected);
  }

  [Theory]
  [InlineData("col VARCHAR(MAX)", "VARCHAR(MAX)")]
  [InlineData("col NVARCHAR(MAX)", "NVARCHAR(MAX)")]
  [InlineData("col VARBINARY(MAX)", "VARBINARY(MAX)")]
  public void Format_MaxTypes_ReturnsMAX(string colSql, string expected)
  {
    DataTypeReference dataType = ParseDataType(colSql);
    DataTypeFormatter.Format(dataType).Should().Be(expected);
  }

  [Fact]
  public void Format_XmlType_ReturnsXML()
  {
    DataTypeReference dataType = ParseDataType("col XML");
    DataTypeFormatter.Format(dataType).Should().Be("XML");
  }
}
