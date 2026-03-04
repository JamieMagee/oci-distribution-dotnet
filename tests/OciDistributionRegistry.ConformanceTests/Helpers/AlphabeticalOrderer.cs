using Xunit.Abstractions;
using Xunit.Sdk;

namespace OciDistributionRegistry.ConformanceTests.Helpers;

/// <summary>
/// Orders test cases alphabetically by method name so that stateful test
/// sequences (A1 → A2, C2 → C3 → C4 → C5 → C6, etc.) execute in order.
/// </summary>
public class AlphabeticalOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        return testCases.OrderBy(tc => tc.TestMethod.Method.Name, StringComparer.Ordinal);
    }
}
