// ============================================================================
// File: Buffer/C2bTestFixtures.cs
// Purpose: Helpers for SqliteBuffer tests — temp file naming, builder
//          shortcuts, and a small assertion helper for the reclaim invariant.
// ============================================================================

using System;
using System.IO;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

internal static class C2bTestFixtures
{
    /// <summary>Temp directory dedicated to this test run.</summary>
    public static string TempDir =>
        Path.Combine(Path.GetTempPath(), "edgeconnect-c2b-tests");

    /// <summary>Allocate a fresh, deletable buffer file path.</summary>
    public static string NewFilePath()
    {
        Directory.CreateDirectory(TempDir);
        var name = $"sqlbuf-{Guid.NewGuid():N}.db";
        return Path.Combine(TempDir, name);
    }

    public static BufferPolicy SmallSqlitePolicy(
        int maxDepth = 64,
        DropPolicy drop = DropPolicy.Block,
        TimeSpan? maxAge = null,
        TimeSpan? reclaimInterval = null) => new()
        {
            Mode = BufferMode.StoreAndForward,
            MaxDepth = maxDepth,
            DropPolicy = drop,
            MaxAge = maxAge,
            // Run the reclaim loop quickly so tests don't have to wait 5s.
            ReclaimInterval = reclaimInterval ?? TimeSpan.FromMilliseconds(50),
        };

    /// <summary>
    /// Try to remove a buffer file and any -wal/-shm sidecars after a test.
    /// Best-effort; tests should not depend on cleanup.
    /// </summary>
    public static void TryDelete(string filePath)
    {
        try { File.Delete(filePath); } catch { }
        try { File.Delete(filePath + "-wal"); } catch { }
        try { File.Delete(filePath + "-shm"); } catch { }
        try { File.Delete(filePath + ".lock"); } catch { }
    }
}
