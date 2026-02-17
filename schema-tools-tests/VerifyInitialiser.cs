using System.Runtime.CompilerServices;

namespace SchemaTools.Tests;

/// <summary>
/// Module initialiser configuring Verify settings for the test assembly.
/// </summary>
internal static class VerifyInitialiser
{
  [ModuleInitializer]
  public static void Initialise()
  {
    VerifierSettings.SortPropertiesAlphabetically();
  }
}
