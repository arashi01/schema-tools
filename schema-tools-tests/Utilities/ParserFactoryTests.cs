using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class ParserFactoryTests
{
  [Theory]
  [InlineData("Sql100", typeof(TSql100Parser))]
  [InlineData("Sql110", typeof(TSql110Parser))]
  [InlineData("Sql120", typeof(TSql120Parser))]
  [InlineData("Sql130", typeof(TSql130Parser))]
  [InlineData("Sql140", typeof(TSql140Parser))]
  [InlineData("Sql150", typeof(TSql150Parser))]
  [InlineData("Sql160", typeof(TSql160Parser))]
  [InlineData("Sql170", typeof(TSql170Parser))]
  public void CreateParser_ValidVersion_ReturnsCorrectParserType(string version, Type expectedType)
  {
    TSqlParser parser = ParserFactory.CreateParser(version);

    parser.Should().BeOfType(expectedType);
  }

  [Theory]
  [InlineData("")]
  [InlineData("Sql90")]
  [InlineData("Sql180")]
  [InlineData("sql170")]
  [InlineData("nonsense")]
  public void CreateParser_UnrecognisedVersion_ThrowsArgumentException(string version)
  {
    Action act = () => ParserFactory.CreateParser(version);

    act.Should().Throw<ArgumentException>()
      .WithMessage($"*{version}*");
  }
}
