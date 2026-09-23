using TUnit.Core.Exceptions;

namespace Subverted.Protocol.Tests;

/// <summary>
/// Both sides compute this independently and have to land on the same string, so the rules are
/// pinned rather than left to whichever one runs first.
/// </summary>
public sealed class DaemonSocketPathTests
{
    [Test]
    public async Task An_override_is_used_verbatim_rather_than_built_on()
    {
        var path = DaemonSocketPath.Resolve("/srv/run/test.sock", "/srv/run");

        await Assert.That(path).IsEqualTo("/srv/run/test.sock");
    }

    /// <summary>
    /// An unset environment variable reads back as null on one platform and as empty on another
    /// once a shell has exported it blank, and neither means "use this path".
    /// </summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task An_absent_or_blank_override_falls_through_to_the_runtime_directory(
        string? socketOverride
    )
    {
        var path = DaemonSocketPath.Resolve(socketOverride, "/srv/run");

        await Assert.That(path).IsEqualTo(Path.Combine("/srv/run", "subverted", "daemon.sock"));
    }

    [Test]
    [Arguments("run")]
    [Arguments("./run")]
    [Arguments("")]
    public async Task A_runtime_directory_that_is_not_absolute_is_refused(string runtimeDirectory)
    {
        await Assert
            .That(() => DaemonSocketPath.Resolve(null, runtimeDirectory))
            .Throws<ArgumentException>();
    }

    /// <summary>The override is honoured even when the runtime directory would be rejected.</summary>
    [Test]
    public async Task An_override_is_checked_before_the_runtime_directory_is()
    {
        var path = DaemonSocketPath.Resolve("/srv/run/test.sock", string.Empty);

        await Assert.That(path).IsEqualTo("/srv/run/test.sock");
    }

    /// <exception cref="SkipTestException">
    /// The override is set in this shell, so the per-user path is not what would be returned and
    /// asserting on it would be asserting on nothing.
    /// </exception>
    [Test]
    public async Task The_environment_resolves_to_this_machines_per_user_socket()
    {
        if (
            Environment.GetEnvironmentVariable(DaemonSocketPath.OverrideVariable) is { Length: > 0 }
        )
        {
            throw new SkipTestException($"{DaemonSocketPath.OverrideVariable} is set here.");
        }

        var path = DaemonSocketPath.FromEnvironment();

        await Assert.That(Path.IsPathRooted(path)).IsTrue();
        await Assert.That(path).EndsWith(Path.Combine("subverted", "daemon.sock"));
    }
}
