// ============================================================================
// File: Configuration/FileSystemConfigurationStoreTests.cs
// Covers: Real filesystem store with atomic writes, drafts, history, audit.
//
// Each test uses a temp directory under Path.GetTempPath() and cleans up
// in IDisposable.Dispose. No tests share state.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class FileSystemConfigurationStoreTests : IDisposable
{
    private readonly string _root;
    private readonly ConfigurationStorageLayout _layout;
    private readonly FileSystemConfigurationStore _store;

    public FileSystemConfigurationStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edgeconnect-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _layout = new ConfigurationStorageLayout(_root);
        _store = new FileSystemConfigurationStore(_layout);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    // ========================================================================
    // current.json
    // ========================================================================

    [Fact]
    public async Task ReadCurrent_Missing_ReturnsNull()
    {
        var content = await _store.ReadCurrentAsync(CancellationToken.None);
        content.Should().BeNull();
    }

    [Fact]
    public async Task WriteCurrent_ThenRead_RoundTrips()
    {
        const string json = """{"hello":"world"}""";
        await _store.WriteCurrentAsync(json, CancellationToken.None);

        var content = await _store.ReadCurrentAsync(CancellationToken.None);
        content.Should().Be(json);
    }

    [Fact]
    public async Task WriteCurrent_Twice_OverwritesPrevious()
    {
        await _store.WriteCurrentAsync("first", CancellationToken.None);
        await _store.WriteCurrentAsync("second", CancellationToken.None);

        var content = await _store.ReadCurrentAsync(CancellationToken.None);
        content.Should().Be("second");
    }

    [Fact]
    public async Task WriteCurrent_AtomicWrite_LeavesNoTempFile()
    {
        await _store.WriteCurrentAsync("hello", CancellationToken.None);

        // After a successful write the .tmp must be gone (it was renamed).
        File.Exists(_layout.CurrentConfigPath + ".tmp").Should().BeFalse();
        File.Exists(_layout.CurrentConfigPath).Should().BeTrue();
    }

    // ========================================================================
    // Drafts
    // ========================================================================

    [Fact]
    public async Task Drafts_RoundTrip()
    {
        var id = DraftId.NewId();
        await _store.WriteDraftAsync(id, "draft-content", CancellationToken.None);

        var content = await _store.ReadDraftAsync(id, CancellationToken.None);
        content.Should().Be("draft-content");

        var ids = await _store.ListDraftsAsync(CancellationToken.None);
        ids.Should().Contain(id);
    }

    [Fact]
    public async Task DeleteDraft_RemovesFile()
    {
        var id = DraftId.NewId();
        await _store.WriteDraftAsync(id, "x", CancellationToken.None);
        await _store.DeleteDraftAsync(id, CancellationToken.None);

        (await _store.ReadDraftAsync(id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteDraft_UnknownId_NoOp()
    {
        var act = async () => await _store.DeleteDraftAsync(DraftId.NewId(), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListDrafts_NoDrafts_ReturnsEmpty()
    {
        var ids = await _store.ListDraftsAsync(CancellationToken.None);
        ids.Should().BeEmpty();
    }

    // ========================================================================
    // History
    // ========================================================================

    [Fact]
    public async Task History_RoundTrip()
    {
        var v = new ConfigurationVersionId("2026-04-08T10-30-00-000Z-001");
        await _store.WriteHistoryAsync(v, "history-content", CancellationToken.None);

        var content = await _store.ReadHistoryAsync(v, CancellationToken.None);
        content.Should().Be("history-content");

        var ids = await _store.ListHistoryAsync(CancellationToken.None);
        ids.Should().Contain(v);
    }

    [Fact]
    public async Task ListHistory_SortedChronologicallyByVersionId()
    {
        var v1 = new ConfigurationVersionId("2026-04-08T10-30-00-000Z-001");
        var v2 = new ConfigurationVersionId("2026-04-08T10-30-00-000Z-002");
        var v3 = new ConfigurationVersionId("2026-04-08T10-30-00-000Z-003");

        // Write in reverse order to verify sort.
        await _store.WriteHistoryAsync(v3, "c", CancellationToken.None);
        await _store.WriteHistoryAsync(v1, "a", CancellationToken.None);
        await _store.WriteHistoryAsync(v2, "b", CancellationToken.None);

        var ids = await _store.ListHistoryAsync(CancellationToken.None);
        ids.Should().Equal(v1, v2, v3);
    }

    [Fact]
    public async Task DeleteHistory_RemovesFile()
    {
        var v = new ConfigurationVersionId("2026-04-08T10-30-00-000Z-001");
        await _store.WriteHistoryAsync(v, "x", CancellationToken.None);

        await _store.DeleteHistoryAsync(v, CancellationToken.None);

        (await _store.ReadHistoryAsync(v, CancellationToken.None)).Should().BeNull();
    }

    // ========================================================================
    // Audit log
    // ========================================================================

    [Fact]
    public async Task AppendAuditLine_ThenRead_RoundTrips()
    {
        await _store.AppendAuditLineAsync("line one", CancellationToken.None);
        await _store.AppendAuditLineAsync("line two", CancellationToken.None);

        var lines = new List<string>();
        await foreach (var line in _store.ReadAuditLinesAsync(CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.Should().Equal("line one", "line two");
    }

    [Fact]
    public async Task AppendAuditLine_WithEmbeddedNewline_Throws()
    {
        var act = async () => await _store.AppendAuditLineAsync("bad\nline", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadAuditLines_MissingFile_ReturnsEmpty()
    {
        var lines = new List<string>();
        await foreach (var line in _store.ReadAuditLinesAsync(CancellationToken.None))
        {
            lines.Add(line);
        }
        lines.Should().BeEmpty();
    }

    [Fact]
    public void Construction_NullLayout_Throws()
    {
        var act = () => new FileSystemConfigurationStore(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
