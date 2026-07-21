using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework(
    "Tests.Integration.HarnessTestFramework", "Tests.Integration")]

namespace Tests.Integration;

/// <summary>
/// The suite's only assembly-wide teardown hook. Test classes run in parallel and each
/// holds a class fixture, so no single fixture's disposal marks "the suite is done" —
/// but the framework object is built once per assembly run and disposed when that run
/// ends, which is exactly when the shared host and browser should go away.
/// </summary>
public class HarnessTestFramework : XunitTestFramework
{
    public HarnessTestFramework(IMessageSink messageSink) : base(messageSink) =>
        // TestFramework.Dispose is not virtual; its disposal tracker is the supported
        // way in.
        DisposalTracker.Add(new HarnessShutdown());

    private sealed class HarnessShutdown : IDisposable
    {
        public void Dispose() => HarnessServer.ShutdownAsync().GetAwaiter().GetResult();
    }
}
