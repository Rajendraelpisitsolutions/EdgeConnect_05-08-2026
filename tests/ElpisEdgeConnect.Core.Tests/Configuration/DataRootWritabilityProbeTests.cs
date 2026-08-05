// ============================================================================
// Tests: DataRootWritabilityProbe (follow-up to the ADR-0020 G5 live finding —
//        an admin-owned, read-only audit.log in ProgramData failed bundle
//        generation deep inside the audit append). The probe must detect that
//        condition at startup so the gateway can surface it as a fault instead.
// ============================================================================

using System;
using System.IO;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class DataRootWritabilityProbeTests : IDisposable
{
    private readonly string _root;
    private readonly ConfigurationStorageLayout _layout;

    public DataRootWritabilityProbeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edc-writeprobe-" + Guid.NewGuid().ToString("N"));
        _layout = new ConfigurationStorageLayout(_root);
        _layout.EnsureDirectoriesExist();
    }

    [Fact]
    public void Probe_WritableDataRoot_ReportsWritable()
    {
        File.WriteAllText(_layout.AuditLogPath, "{}\n");
        File.WriteAllText(_layout.CurrentConfigPath, "{}");

        var result = DataRootWritabilityProbe.Probe(_layout);

        result.IsWritable.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Probe_LeavesNoArtifacts_AndDoesNotModifyFiles()
    {
        File.WriteAllText(_layout.AuditLogPath, "original-audit\n");
        File.WriteAllText(_layout.CurrentConfigPath, "original-config");

        DataRootWritabilityProbe.Probe(_layout);

        // Content untouched (write-open probes never write) and no probe file left behind.
        File.ReadAllText(_layout.AuditLogPath).Should().Be("original-audit\n");
        File.ReadAllText(_layout.CurrentConfigPath).Should().Be("original-config");
        Directory.GetFiles(_layout.HistoryDirectory, ".write-probe-*").Should().BeEmpty();
    }

    [Fact]
    public void Probe_ReadOnlyAuditLog_ReportsNotWritable()
    {
        File.WriteAllText(_layout.AuditLogPath, "{}\n");
        File.SetAttributes(_layout.AuditLogPath, FileAttributes.ReadOnly);
        try
        {
            // Guard: if this process can still append despite the ReadOnly
            // attribute (e.g. running as root on Linux), the probe genuinely
            // can't detect a problem — skip the assertion rather than fail.
            if (CanAppend(_layout.AuditLogPath))
            {
                return;
            }

            var result = DataRootWritabilityProbe.Probe(_layout);

            result.IsWritable.Should().BeFalse();
            result.Issues.Should().ContainSingle()
                .Which.Should().Contain("audit.log").And.Contain("append");
        }
        finally
        {
            File.SetAttributes(_layout.AuditLogPath, FileAttributes.Normal);
        }
    }

    private static bool CanAppend(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
            }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_layout.AuditLogPath))
            {
                File.SetAttributes(_layout.AuditLogPath, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
        catch { /* best-effort */ }
    }
}
