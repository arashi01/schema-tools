using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class ParserFactoryTests
{
  [Theory]
  [InlineData(SqlServerVersion.Sql100, typeof(TSql100Parser))]
  [InlineData(SqlServerVersion.Sql110, typeof(TSql110Parser))]
  [InlineData(SqlServerVersion.Sql120, typeof(TSql120Parser))]
  [InlineData(SqlServerVersion.Sql130, typeof(TSql130Parser))]
  [InlineData(SqlServerVersion.Sql140, typeof(TSql140Parser))]
  [InlineData(SqlServerVersion.Sql150, typeof(TSql150Parser))]
  [InlineData(SqlServerVersion.Sql160, typeof(TSql160Parser))]
  [InlineData(SqlServerVersion.Sql170, typeof(TSql170Parser))]
  public void CreateParser_ValidVersion_ReturnsCorrectParserType(SqlServerVersion version, Type expectedType)
  {
    OperationResult<TSqlParser> result = ParserFactory.CreateParser(version);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().BeOfType(expectedType);
  }

  [Theory]
  [InlineData((SqlServerVersion)(-1))]
  [InlineData((SqlServerVersion)999)]
  public void CreateParser_UnrecognisedVersion_ReturnsFailure(SqlServerVersion version)
  {
    OperationResult<TSqlParser> result = ParserFactory.CreateParser(version);

    result.IsSuccess.Should().BeFalse();
    result.HasErrors.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle()
      .Which.Should().BeOfType<GenerationError>()
      .Which.Code.Should().Be("ST3003");
  }

  [Fact]
  public void CreateParser_SameVersion_ReturnsCachedInstance()
  {
    OperationResult<TSqlParser> first = ParserFactory.CreateParser(SqlServerVersion.Sql170);
    OperationResult<TSqlParser> second = ParserFactory.CreateParser(SqlServerVersion.Sql170);

    first.IsSuccess.Should().BeTrue();
    second.IsSuccess.Should().BeTrue();
    first.Value.Should().BeSameAs(second.Value);
  }
}
