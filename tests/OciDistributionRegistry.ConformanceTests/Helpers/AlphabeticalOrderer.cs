using Xunit.v3;

namespace OciDistributionRegistry.ConformanceTests.Helpers;

/// <summary>
/// Orders test cases alphabetically by method name so that stateful test
/// sequences (A1 → A2, C2 → C3 → C4 → C5 → C6, etc.) execute in order.
/// </summary>
public class AlphabeticalOrderer : ITestCaseOrderer
{
    IReadOnlyCollection<TTestCase> ITestCaseOrderer.OrderTestCases<TTestCase>(
        IReadOnlyCollection<TTestCase> testCases
    )
    {
        return testCases.OrderBy(tc => tc.TestMethod?.MethodName, StringComparer.Ordinal).ToList();
    }
}
