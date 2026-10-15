using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Acceptance;

/// <summary>
/// Compares a value with its recording for the current operating system, or records it.
/// </summary>
internal static class RecordedOutput
{
    private static readonly bool _record = Environment.GetEnvironmentVariable("JELLYFIN_ACCEPTANCE_RECORD") == "1";

    public static void Verify(string suite, string caseId, string actual, [CallerFilePath] string callerPath = "")
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var directory = Path.Join(Path.GetDirectoryName(callerPath), "Recorded", suite, os);
        var file = Path.Join(directory, caseId + ".txt");

        if (_record)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(file, actual + "\n");
            return;
        }

        if (!File.Exists(file))
        {
            Assert.Skip($"No recording for {os}: {file}. Record with JELLYFIN_ACCEPTANCE_RECORD=1.");
        }

        Assert.Equal(File.ReadAllText(file).TrimEnd('\n'), actual);
    }
}
