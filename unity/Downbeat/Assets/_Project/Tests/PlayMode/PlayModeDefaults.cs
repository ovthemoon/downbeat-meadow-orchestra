using System;
using Downbeat.Core;
using NUnit.Framework;

/// <summary>
/// Options every PlayMode test starts with unless it says otherwise (the fixture has no namespace, so NUnit runs it once for the whole assembly). The shipped content has songs, so without
/// <c>--songs off</c> every test that starts the scene would play the songs instead of the free play it was written for; the tests of song mode name <c>--songs on</c> themselves. The options only
/// fill what a test left out, see <see cref="LaunchArgs.DefaultsForTests"/>.
/// </summary>
[SetUpFixture]
public sealed class PlayModeDefaults
{
    IDisposable _defaults;

    [OneTimeSetUp]
    public void ApplyDefaults()
    {
        _defaults = LaunchArgs.DefaultsForTests(new[] { "--songs", "off" });
    }

    [OneTimeTearDown]
    public void RestoreDefaults()
    {
        _defaults?.Dispose();
        _defaults = null;
    }
}
