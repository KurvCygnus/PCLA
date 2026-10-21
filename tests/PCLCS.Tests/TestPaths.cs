namespace PCLCS.Tests;

internal static class TestPaths
{
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Expected(string name) => Path.Combine(AppContext.BaseDirectory, "Expected", name);
}
