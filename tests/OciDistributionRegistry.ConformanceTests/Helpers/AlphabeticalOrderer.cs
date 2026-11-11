using Xunit.v3;

namespace OciDistributionRegistry.ConformanceTests.Helpers;

/// <summary>
/// Orders test methods alphabetically by name so that stateful test
/// sequences (A1 → A2, C2 → C3 → C4 → C5 → C6, etc.) execute in order.
/// </summary>
public class AlphabeticalOrderer : ITestMethodOrderer
{
    IReadOnlyCollection<TTestMethod?> ITestMethodOrderer.OrderTestMethods<TTestMethod>(
        IReadOnlyCollection<TTestMethod?> testMethods
    )
        where TTestMethod : default
    {
        return testMethods.OrderBy(tm => tm?.MethodName, StringComparer.Ordinal).ToList();
    }
}
