[assembly: AssemblyFixture(typeof(Tests.Integration.HarnessShutdown))]

namespace Tests.Integration;

/// <summary>
/// The suite's only assembly-wide teardown hook. Test classes run in parallel and each
/// holds a class fixture, so no single fixture's disposal marks "the suite is done" —
/// but an assembly fixture is disposed exactly once, after every test class has
/// finished, which is when the shared host and browser should go away.
/// </summary>
public sealed class HarnessShutdown : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await HarnessServer.ShutdownAsync();
}
