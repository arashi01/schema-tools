using SchemaTools.Extraction;

namespace SchemaTools;

/// <summary>
/// CLI entry point for metadata extraction via dotnet exec.
/// Used by Full Framework MSBuild (msbuild.exe) where DacFx cannot run in-process
/// due to SSDT assembly identity collisions. The .targets file invokes this as:
///   dotnet exec SchemaTools.dll extract-metadata --dacpac &lt;path&gt; --output &lt;path&gt; [--config &lt;path&gt;]
/// </summary>
internal static class Program
{
  internal static int Main(string[] args)
  {
    if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
    {
      PrintUsage();
      return args.Length == 0 ? 1 : 0;
    }

    if (!string.Equals(args[0], "extract-metadata", StringComparison.OrdinalIgnoreCase))
    {
      Console.Error.WriteLine($"Unknown command: {args[0]}");
      PrintUsage();
      return 1;
    }

    string? dacpacPath = null;
    string? outputFile = null;
    string? configFile = null;
    string databaseName = "Database";

    // Parse named arguments
    for (int i = 1; i < args.Length; i++)
    {
      string arg = args[i];
      string? next = i + 1 < args.Length ? args[i + 1] : null;

      switch (arg)
      {
        case "--dacpac" when next != null:
          dacpacPath = next;
          i++;
          break;
        case "--output" when next != null:
          outputFile = next;
          i++;
          break;
        case "--config" when next != null:
          configFile = next;
          i++;
          break;
        case "--database" when next != null:
          databaseName = next;
          i++;
          break;
        default:
          Console.Error.WriteLine($"Unknown argument: {arg}");
          PrintUsage();
          return 1;
      }
    }

    if (string.IsNullOrEmpty(dacpacPath))
    {
      Console.Error.WriteLine("Missing required argument: --dacpac");
      return 1;
    }

    if (string.IsNullOrEmpty(outputFile))
    {
      Console.Error.WriteLine("Missing required argument: --output");
      return 1;
    }

    var engine = new DacpacMetadataEngine(
      dacpacPath,
      outputFile,
      configFile ?? string.Empty,
      databaseName,
      info: Console.WriteLine,
      verbose: _ => { }, // suppress verbose output in CLI mode
      warning: msg => Console.Error.WriteLine($"warning : {msg}"),
      error: msg => Console.Error.WriteLine($"error : {msg}"));

    return engine.Execute() ? 0 : 1;
  }

  private static void PrintUsage()
  {
    Console.Error.WriteLine("Usage: dotnet exec SchemaTools.dll extract-metadata --dacpac <path> --output <path> [--config <path>] [--database <name>]");
  }
}
