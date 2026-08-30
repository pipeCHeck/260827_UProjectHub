using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

[TestClass]
public sealed class WebUrlLauncherTests
{
    [TestMethod]
    public void HttpsUrlOpensThroughShellWithoutArguments()
    {
        var process = new RecordingProcessLauncher();
        var launcher = new WebUrlLauncher(process);

        var result = launcher.Open("https://git.example.com/team/game");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(process.Request);
        Assert.AreEqual(
            "https://git.example.com/team/game",
            process.Request.FileName);
        Assert.IsTrue(process.Request.UseShellExecute);
        Assert.IsEmpty(process.Request.ArgumentList);
    }

    [TestMethod]
    [DataRow("file:///C:/secrets")]
    [DataRow("javascript:alert(1)")]
    [DataRow("https://user:password@example.com/repo")]
    [DataRow("git@example.com:team/game.git")]
    public void UnsafeOrNonWebRemoteNeverReachesShell(string url)
    {
        var process = new RecordingProcessLauncher();
        var launcher = new WebUrlLauncher(process);

        var result = launcher.Open(url);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(process.Request);
    }

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public ProcessRequest? Request { get; private set; }

        public LaunchResult Launch(ProcessRequest request)
        {
            Request = request;
            return LaunchResult.Succeeded();
        }
    }
}
