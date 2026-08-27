using UProjectHub.Windows.Launching;

namespace UProjectHub.Core.Tests.Windows.Launching;

internal sealed class FakeProcessLauncher(LaunchResult result) : IProcessLauncher
{
    private readonly List<ProcessRequest> _requests = [];

    public IReadOnlyList<ProcessRequest> Requests => _requests.AsReadOnly();

    public LaunchResult Launch(ProcessRequest request)
    {
        _requests.Add(request);
        return result;
    }
}
