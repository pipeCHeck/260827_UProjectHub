[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace UProjectHub.Core.Tests;

[TestClass]
public sealed class SolutionSmokeTests
{
    [TestMethod]
    public void TestAssemblyLoads()
    {
        Assert.AreEqual(
            "UProjectHub.Core.Tests",
            typeof(SolutionSmokeTests).Assembly.GetName().Name);
    }
}
