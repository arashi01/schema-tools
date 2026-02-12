using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Utilities;

/// <summary>
/// Formats SQL Server data types for display
/// </summary>
public static class DataTypeFormatter
{
  public static string Format(DataTypeReference dataType)
  {
    return dataType switch
    {
      SqlDataTypeReference sqlType => FormatSqlDataType(sqlType),
      UserDataTypeReference userType => FormatUserDataType(userType),
      XmlDataTypeReference => "XML",
      _ => dataType.ToString() ?? "UNKNOWN"
    };
  }

  private static string FormatSqlDataType(SqlDataTypeReference sqlType)
  {
    string typeName = sqlType.SqlDataTypeOption.ToString().ToUpperInvariant();

    if (sqlType.Parameters == null || sqlType.Parameters.Count == 0)
    {
      return typeName;
    }

    string parameters = string.Join(", ", sqlType.Parameters.Select(FormatParameter));
    return $"{typeName}({parameters})";
  }

  private static string FormatParameter(Literal parameter)
  {
    if (parameter is MaxLiteral)
    {
      return "MAX";
    }

    return parameter.Value;
  }

  private static string FormatUserDataType(UserDataTypeReference userType)
  {
    string name = userType.Name.BaseIdentifier.Value;

    if (userType.Name.SchemaIdentifier != null)
    {
      return $"[{userType.Name.SchemaIdentifier.Value}].[{name}]";
    }

    return name;
  }
}
