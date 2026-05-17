// -----------------------------------------------------------------------
// <copyright file="RequiresDockerFactAttribute.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

// Mirror of compendium-adapter-pgvector / RequiresDockerFactAttribute. Same
// detection (calls `docker info`), same skip semantics — keeps the integration
// suite behaviourally identical across adapters.

using Xunit;

namespace Compendium.Adapters.Qdrant.IntegrationTests.Fixtures;

/// <summary>
/// xUnit fact attribute that skips the test when Docker is unavailable.
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerDetection.IsDockerAvailable)
        {
            Skip = "Docker is not running or misconfigured. Start Docker to run integration tests.";
        }
    }
}

internal static class DockerDetection
{
    private static readonly Lazy<bool> s_isAvailable = new(DetectDocker);

    public static bool IsDockerAvailable => s_isAvailable.Value;

    private static bool DetectDocker()
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
