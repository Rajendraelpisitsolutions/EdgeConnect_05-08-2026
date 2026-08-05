// ============================================================================
// File: Store/SqliteSparkplugIdentityStateStoreTests.cs
// Purpose: Locks K3 slice-2 identity store (review r2). The K0 WS5 bdSeq matrix
//          against the production store plus the review boundaries: R1 SQLite
//          storage-class validation (typeof) + wrapped key corruption; R2 full
//          current-schema fingerprint validation (CHECK/UNIQUE/COLLATE/NOT NULL) +
//          exact version/table set + WAL/FULL/busy readback; F3 ordinal/culture
//          identity. Uses Core.Configuration.BrokerEndpoint as the shared endpoint.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Store;

public sealed class SqliteSparkplugIdentityStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-idstore-" + Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_dir, "sparkplug", "identity-state.db");
    private string ConnString => new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

    private const string BrokerKey = "mqtt://b1:1883"; // Core BrokerEndpoint.ToString() for ("b1", 1883, tls:false)

    private static readonly SparkplugEdgeNodeIdentity Node = SparkplugEdgeNodeIdentity.Create("groupA", "edgeNode1");
    private static readonly SparkplugStoreIdentity Id =
        SparkplugStoreIdentity.Create(BrokerEndpoint.Create("b1", 1883, tls: false), Node);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
        catch { /* best effort */ }
    }

    private SqliteSparkplugIdentityStateStore NewStore() => new(DbPath);

    // Skips existing-schema fingerprint validation, to exercise the runtime read-checks
    // over a deliberately damaged (but openable) schema.
    private SqliteSparkplugIdentityStateStore SeamStore() => new(DbPath, validateExistingSchema: false);

    // ==== bdSeq crash-safety matrix (K0 WS5, against the production store) ====

    [Fact]
    public void ReserveBdSeq_InitialIncrementAndRestart()
    {
        var store = NewStore();
        store.ReserveNextBdSeq(Id).Value.Should().Be(0);
        store.ReserveNextBdSeq(Id).Value.Should().Be(1);
        store.ReserveNextBdSeq(Id).Value.Should().Be(2);
        NewStore().ReserveNextBdSeq(Id).Value.Should().Be(3);
    }

    [Fact]
    public void ReserveBdSeq_CommittedBeforeReturn()
    {
        var reserved = NewStore().ReserveNextBdSeq(Id);
        NewStore().PeekBdSeqCounter(Id).Should().Be(reserved.Value);
    }

    [Fact]
    public void ReserveBdSeq_CommittedButUnused_SkippedNeverReused()
    {
        var reserved = NewStore().ReserveNextBdSeq(Id);
        ((int)NewStore().ReserveNextBdSeq(Id).Value).Should().Be(reserved.Value + 1);
    }

    [Fact]
    public async Task ReserveBdSeq_TwoInstancesConcurrently_ReserveUniqueValues()
    {
        var a = NewStore();
        var b = NewStore();
        var tasks = Enumerable.Range(0, 200)
            .Select(i => Task.Run(() => (int)(i % 2 == 0 ? a : b).ReserveNextBdSeq(Id).Value));
        var results = await Task.WhenAll(tasks);

        results.Distinct().Should().HaveCount(200);
        results.OrderBy(x => x).Should().Equal(Enumerable.Range(0, 200));
    }

    [Fact]
    public void ReserveBdSeq_PreCommitFault_ReturnsNoValueAndDoesNotAdvance()
    {
        var store = NewStore();
        store.ReserveNextBdSeq(Id);
        store.ReserveNextBdSeq(Id);

        store.PreCommitFault = () => throw new InvalidOperationException("crash before commit");
        store.Invoking(s => s.ReserveNextBdSeq(Id)).Should().Throw<InvalidOperationException>();

        store.PreCommitFault = null;
        NewStore().PeekBdSeqCounter(Id).Should().Be(1);
        store.ReserveNextBdSeq(Id).Value.Should().Be(2);
    }

    [Fact]
    public void ReserveBdSeq_WrapsAt256_ButCounterNeverReused()
    {
        var store = NewStore();
        byte last = 0;
        for (var i = 0; i < 257; i++)
        {
            last = store.ReserveNextBdSeq(Id).Value;
        }

        last.Should().Be(0);
        NewStore().PeekBdSeqCounter(Id).Should().Be(256);
        store.ReserveNextBdSeq(Id).Value.Should().Be(1);
    }

    [Fact]
    public void ReserveBdSeq_DifferentIdentities_Independent()
    {
        var store = NewStore();
        store.ReserveNextBdSeq(Identity("bx", node: "nodeA")).Value.Should().Be(0);
        store.ReserveNextBdSeq(Identity("bx", node: "nodeA")).Value.Should().Be(1);
        store.ReserveNextBdSeq(Identity("bx", node: "nodeB")).Value.Should().Be(0);
    }

    // ==== R1 — bdSeq storage-class + range validation (runtime read, via seam) ====

    [Theory]
    [InlineData(-1L)]
    [InlineData(-5L)]
    [InlineData(long.MaxValue)]
    public void ReserveBdSeq_OutOfRangeIntegerCounter_FailsClosedWithoutAdvancing(long corrupt)
    {
        NewStore().ReserveNextBdSeq(Id);
        SetBdSeqRaw(corrupt);

        SeamStore().Invoking(s => s.ReserveNextBdSeq(Id)).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
        SeamStore().PeekBdSeqCounter(Id).Should().Be(corrupt); // integer storage class, unchanged
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.5)]
    public void ReserveBdSeq_RealStorageClassCounter_FailsClosed(double corrupt)
    {
        NewStore().ReserveNextBdSeq(Id);
        SetBdSeqRaw(corrupt);

        SeamStore().Invoking(s => s.ReserveNextBdSeq(Id)).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
        Convert.ToDouble(RawScalar("SELECT last_counter FROM bdseq;"), CultureInfo.InvariantCulture)
            .Should().Be(corrupt); // not advanced
    }

    [Theory]
    [InlineData("123")]        // numeric-looking TEXT
    [InlineData("not-a-num")]
    public void ReserveBdSeq_TextStorageClassCounter_FailsClosed(string corrupt)
    {
        NewStore().ReserveNextBdSeq(Id);
        SetBdSeqRaw(corrupt, counterType: "BLOB"); // BLOB affinity keeps the string as TEXT (no integer coercion)

        SeamStore().Invoking(s => s.ReserveNextBdSeq(Id)).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    // ==== B2 — schema classification, PRAGMAs, path ====

    [Fact]
    public void Construct_FreshDatabase_CreatesAllTablesAndStampsVersion()
    {
        NewStore();
        TableExistsRaw("meta").Should().BeTrue();
        TableExistsRaw("bdseq").Should().BeTrue();
        TableExistsRaw("alias").Should().BeTrue();
        (RawScalar("SELECT value FROM meta WHERE key='schema_version';") as string).Should().Be("1");
    }

    [Fact]
    public void EffectivePragmas_AreWalFullAndBusyTimeout()
    {
        var (journal, synchronous, busy) = NewStore().ReadEffectivePragmas();
        journal.Should().Be("wal");
        synchronous.Should().Be(2); // FULL
        busy.Should().Be(5000);
    }

    [Fact]
    public void Construct_MetaWithoutVersionRow_FailsClosed()
    {
        RawExec("CREATE TABLE meta (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);");
        SqliteConnection.ClearAllPools();
        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void Construct_MalformedVersion_FailsClosed()
    {
        RawExec("CREATE TABLE meta (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);");
        RawExec("INSERT INTO meta VALUES ('schema_version', 'abc');");
        SqliteConnection.ClearAllPools();
        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void Construct_FutureVersion_FailsClosed_AndAddsNoTables()
    {
        RawExec("CREATE TABLE meta (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);");
        RawExec("INSERT INTO meta VALUES ('schema_version', '999');");
        SqliteConnection.ClearAllPools();
        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreSchemaUnsupported);
        TableExistsRaw("bdseq").Should().BeFalse();
    }

    [Fact]
    public void Construct_MissingTable_FailsClosed_NotRepaired()
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec("DROP TABLE alias;");
        SqliteConnection.ClearAllPools();
        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
        TableExistsRaw("alias").Should().BeFalse();
    }

    [Fact]
    public void Construct_ExtraTable_FailsClosed()
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec("CREATE TABLE extra (x INTEGER);");
        SqliteConnection.ClearAllPools();
        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void Construct_RelativePath_Rejected()
    {
        ((Action)(() => new SqliteSparkplugIdentityStateStore(Path.Combine("relative", "identity-state.db"))))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_InaccessibleDirectory_FailsTyped()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var underFile = Path.Combine(blocker, "sub", "identity-state.db");

        ((Action)(() => new SqliteSparkplugIdentityStateStore(underFile))).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void CorruptState_FailsClosed_NeverResetsToZero()
    {
        NewStore().ReserveNextBdSeq(Id);
        SqliteConnection.ClearAllPools();
        File.WriteAllText(DbPath, "this is not a database");
        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    // ==== R2 — full current-schema invariant validation (fingerprint, ctor) ====

    [Theory]
    // missing counter CHECK
    [InlineData("bdseq", "CREATE TABLE bdseq (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, last_counter INTEGER NOT NULL, PRIMARY KEY (broker, grp, node));")]
    // identity column collation weakened to NOCASE
    [InlineData("bdseq", "CREATE TABLE bdseq (broker TEXT NOT NULL COLLATE NOCASE, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, last_counter INTEGER NOT NULL CHECK (typeof(last_counter) = 'integer' AND last_counter >= 0), PRIMARY KEY (broker, grp, node));")]
    // required column made nullable
    [InlineData("bdseq", "CREATE TABLE bdseq (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, last_counter INTEGER CHECK (typeof(last_counter) = 'integer' AND last_counter >= 0), PRIMARY KEY (broker, grp, node));")]
    // extra column
    [InlineData("bdseq", "CREATE TABLE bdseq (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, last_counter INTEGER NOT NULL CHECK (typeof(last_counter) = 'integer' AND last_counter >= 0), extra INTEGER, PRIMARY KEY (broker, grp, node));")]
    // missing alias CHECK
    [InlineData("alias", "CREATE TABLE alias (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, source TEXT NOT NULL COLLATE BINARY, device TEXT NOT NULL COLLATE BINARY, tagpath TEXT NOT NULL COLLATE BINARY, alias INTEGER NOT NULL, PRIMARY KEY (broker, grp, node, source, device, tagpath), UNIQUE (broker, grp, node, alias));")]
    // missing UNIQUE alias index (clean rows)
    [InlineData("alias", "CREATE TABLE alias (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, source TEXT NOT NULL COLLATE BINARY, device TEXT NOT NULL COLLATE BINARY, tagpath TEXT NOT NULL COLLATE BINARY, alias INTEGER NOT NULL CHECK (typeof(alias) = 'integer' AND alias >= 1), PRIMARY KEY (broker, grp, node, source, device, tagpath));")]
    // missing alias PRIMARY KEY
    [InlineData("alias", "CREATE TABLE alias (broker TEXT, grp TEXT, node TEXT, source TEXT, device TEXT, tagpath TEXT, alias INTEGER);")]
    // altered storage-class literal 'INTEGER' (case-significant; must NOT pass) — bdseq
    [InlineData("bdseq", "CREATE TABLE bdseq (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, last_counter INTEGER NOT NULL CHECK (typeof(last_counter) = 'INTEGER' AND last_counter >= 0), PRIMARY KEY (broker, grp, node));")]
    // altered storage-class literal 'INTEGER' — alias
    [InlineData("alias", "CREATE TABLE alias (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, node TEXT NOT NULL COLLATE BINARY, source TEXT NOT NULL COLLATE BINARY, device TEXT NOT NULL COLLATE BINARY, tagpath TEXT NOT NULL COLLATE BINARY, alias INTEGER NOT NULL CHECK (typeof(alias) = 'INTEGER' AND alias >= 1), PRIMARY KEY (broker, grp, node, source, device, tagpath), UNIQUE (broker, grp, node, alias));")]
    public void Construct_DamagedCurrentSchema_FailsClosed(string table, string deviantDdl)
    {
        NewStore(); // valid v1
        SqliteConnection.ClearAllPools();
        RawExec($"DROP TABLE {table};");
        RawExec(deviantDdl);
        SqliteConnection.ClearAllPools();

        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    // ==== R3 — non-table object validation (triggers / views / explicit indexes) ====

    [Fact]
    public void Construct_UnrelatedViewInEmptyFile_NotFresh_AndAddsNoTables()
    {
        RawExec("CREATE VIEW v AS SELECT 1;");
        SqliteConnection.ClearAllPools();

        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
        TableExistsRaw("bdseq").Should().BeFalse(); // never seeded into a non-clean file
    }

    [Theory]
    [InlineData("CREATE TRIGGER t_bd AFTER INSERT ON bdseq BEGIN DELETE FROM bdseq; END;")]
    [InlineData("CREATE TRIGGER t_al AFTER INSERT ON alias BEGIN DELETE FROM alias; END;")]
    public void Construct_ValidStorePlusTrigger_FailsClosed(string triggerDdl)
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec(triggerDdl);
        SqliteConnection.ClearAllPools();

        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void Construct_ValidStorePlusExplicitIndex_FailsClosed()
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec("CREATE INDEX idx_bd ON bdseq(last_counter);");
        SqliteConnection.ClearAllPools();

        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Theory] // 'sqliteX...' names are LEGAL user objects (only the literal 'sqlite_' prefix is reserved);
             // a 'NOT LIKE sqlite_%' filter would wrongly hide these ('_' is a LIKE wildcard) — r4.
    [InlineData("CREATE TRIGGER sqliteXdelete AFTER INSERT ON bdseq BEGIN DELETE FROM bdseq; END;")]
    [InlineData("CREATE VIEW sqliteXview AS SELECT 1;")]
    [InlineData("CREATE INDEX sqliteXindex ON bdseq(last_counter);")]
    public void Construct_ValidStorePlusSqliteLookalikeObject_FailsClosed(string objectDdl)
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec(objectDdl);
        SqliteConnection.ClearAllPools();

        Constructing().Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    // ==== Alias allocation (happy paths) ====

    [Fact]
    public void ResolveAliases_AllocatesFromOne_DeterministicOrdinalOrder()
    {
        var map = NewStore().ResolveAliases(Id, new[] { Key("srcC"), Key("srcA"), Key("srcB") });
        map[Key("srcA")].Should().Be(1);
        map[Key("srcB")].Should().Be(2);
        map[Key("srcC")].Should().Be(3);
    }

    [Fact]
    public void ResolveAliases_EmptyManifest_ReturnsEmptyImmutable()
    {
        var map = NewStore().ResolveAliases(Id, Array.Empty<SparkplugAliasKey>());
        map.Should().BeEmpty();
        (map as Dictionary<SparkplugAliasKey, ulong>).Should().BeNull();
    }

    [Fact]
    public void ResolveAliases_StableAcrossReopen_AndGrowth()
    {
        var first = NewStore().ResolveAliases(Id, new[] { Key("srcA"), Key("srcB") });
        first[Key("srcA")].Should().Be(1);
        first[Key("srcB")].Should().Be(2);

        var second = NewStore().ResolveAliases(Id, new[] { Key("srcA"), Key("srcB"), Key("srcC") });
        second[Key("srcA")].Should().Be(1);
        second[Key("srcB")].Should().Be(2);
        second[Key("srcC")].Should().Be(3);
    }

    [Fact]
    public void ResolveAliases_AbsentThenReappearing_KeepsSameAlias()
    {
        NewStore().ResolveAliases(Id, new[] { Key("srcA"), Key("srcB") });
        NewStore().ResolveAliases(Id, new[] { Key("srcB") });
        NewStore().ResolveAliases(Id, new[] { Key("srcA"), Key("srcB") })[Key("srcA")].Should().Be(1);
    }

    [Fact]
    public void ResolveAliases_DuplicateIdenticalKeys_GetOneAlias()
    {
        var map = NewStore().ResolveAliases(Id, new[] { Key("s"), Key("s") });
        map.Should().HaveCount(1);
        map[Key("s")].Should().Be(1);
    }

    [Fact]
    public void ResolveAliases_ReturnedMapIsImmutable()
    {
        (NewStore().ResolveAliases(Id, new[] { Key("srcA") }) as Dictionary<SparkplugAliasKey, ulong>)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveAliases_DifferentNodes_Independent()
    {
        var store = NewStore();
        store.ResolveAliases(Identity("bx", node: "nodeA"), new[] { Key("s") })[Key("s")].Should().Be(1);
        store.ResolveAliases(Identity("bx", node: "nodeB"), new[] { Key("s") })[Key("s")].Should().Be(1);
    }

    [Fact]
    public void ResolveAliases_PreCommitFault_RollsBackWholeBatch()
    {
        var store = NewStore();
        store.AliasPreCommitFault = () => throw new InvalidOperationException("boom");
        store.Invoking(s => s.ResolveAliases(Id, new[] { Key("srcA"), Key("srcB") }))
            .Should().Throw<InvalidOperationException>();

        store.AliasPreCommitFault = null;
        NewStore().ResolveAliases(Id, new[] { Key("srcA") })[Key("srcA")].Should().Be(1);
    }

    [Fact]
    public async Task ResolveAliases_ConcurrentGrowth_ProducesOneStableNodeUniqueMap()
    {
        var a = NewStore();
        var b = NewStore();
        var shared = Key("s0");
        var t1 = Task.Run(() => a.ResolveAliases(Id, new[] { shared, Key("s1") }));
        var t2 = Task.Run(() => b.ResolveAliases(Id, new[] { shared, Key("s2") }));
        await Task.WhenAll(t1, t2);

        t1.Result[shared].Should().Be(t2.Result[shared]);

        var final = NewStore().ResolveAliases(Id, new[] { shared, Key("s1"), Key("s2") });
        final.Values.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void ResolveAliases_AliasAtLongMaxValue_NewAllocationFailsTyped_AndCommitsNoRow()
    {
        NewStore().ResolveAliases(Id, new[] { Key("keyA") }); // alias 1
        RawExec($"UPDATE alias SET alias = {long.MaxValue} WHERE source = 'keyA';"); // valid integer >= 1

        NewStore().Invoking(s => s.ResolveAliases(Id, new[] { Key("keyA"), Key("keyB") }))
            .Should().Throw<AdapterException>();
        RawScalar("SELECT COUNT(*) FROM alias;").Should().Be(1L); // keyB not committed
    }

    // ==== R1 — alias storage-class / corruption (runtime read, via seam) ====

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void ResolveAliases_InvalidIntegerAlias_FailsClosed(long corrupt)
    {
        NewStore().ResolveAliases(Id, new[] { Key("srcA") });
        SetAliasRaw(corrupt, keepUnique: true);
        SeamStore().Invoking(s => s.ResolveAliases(Id, new[] { Key("srcA") })).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void ResolveAliases_RealStorageClassAlias_FailsClosed()
    {
        NewStore().ResolveAliases(Id, new[] { Key("srcA") });
        SetAliasRaw(1.5, keepUnique: true);
        SeamStore().Invoking(s => s.ResolveAliases(Id, new[] { Key("srcA") })).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void ResolveAliases_DuplicateAliasValue_FailsClosed()
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec("DROP TABLE alias;");
        RawExec(AliasTableSql(withUnique: false, withPk: true));
        RawExec($"INSERT INTO alias VALUES ('{BrokerKey}','groupA','edgeNode1','sX','dev','temp',5)," +
                $"('{BrokerKey}','groupA','edgeNode1','sY','dev','temp',5);");
        SqliteConnection.ClearAllPools();

        SeamStore().Invoking(s => s.ResolveAliases(Id, new[] { Key("sX"), Key("sY") })).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void ResolveAliases_DuplicateCanonicalKey_FailsClosed()
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec("DROP TABLE alias;");
        RawExec(AliasTableSql(withUnique: false, withPk: false));
        RawExec($"INSERT INTO alias VALUES ('{BrokerKey}','groupA','edgeNode1','sX','dev','temp',1)," +
                $"('{BrokerKey}','groupA','edgeNode1','sX','dev','temp',2);");
        SqliteConnection.ClearAllPools();

        SeamStore().Invoking(s => s.ResolveAliases(Id, new[] { Key("sX") })).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable);
    }

    [Fact]
    public void ResolveAliases_MalformedPersistedKeyComponent_FailsAsStoreCorruption()
    {
        NewStore();
        SqliteConnection.ClearAllPools();
        RawExec("DROP TABLE alias;");
        RawExec(AliasTableSql(withUnique: true, withPk: true));
        // A source containing '/' is invalid for SparkplugAliasKey — persisted corruption.
        RawExec($"INSERT INTO alias VALUES ('{BrokerKey}','groupA','edgeNode1','bad/src','dev','temp',1);");
        SqliteConnection.ClearAllPools();

        SeamStore().Invoking(s => s.ResolveAliases(Id, new[] { Key("srcA") })).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityStoreUnavailable); // NOT IDENTITY_INVALID
    }

    // ==== F3 — ordinal / culture-insensitive identity ====

    [Fact]
    public void ResolveAliases_CaseOnlyDifference_GetsDistinctAliases()
    {
        var map = NewStore().ResolveAliases(Id, new[] { Key("src", device: "dev"), Key("src", device: "Dev") });
        map[Key("src", device: "dev")].Should().NotBe(map[Key("src", device: "Dev")]);
        map.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveAliases_SeparateColumns_TagPathSlashesDoNotCollide()
    {
        var k1 = Key("s", "d", "a/b/c");
        var k2 = Key("s", "d", "a/b");
        var map = NewStore().ResolveAliases(Id, new[] { k1, k2 });
        map.Should().HaveCount(2);
        map[k1].Should().NotBe(map[k2]);
    }

    [Fact]
    public void ResolveAliases_CultureInsensitive_IncludingTurkish()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var map = NewStore().ResolveAliases(Id, new[] { Key("i"), Key("I") });
            map.Should().HaveCount(2); // ordinal "i" != "I" even under Turkish culture

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var reopened = NewStore().ResolveAliases(Id, new[] { Key("i"), Key("I") });
            reopened[Key("i")].Should().Be(map[Key("i")]);
            reopened[Key("I")].Should().Be(map[Key("I")]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ==== B4 — canonical broker-endpoint identity ====

    [Fact]
    public void Endpoint_HostCaseDifference_IsSameIdentity()
    {
        var store = NewStore();
        store.ReserveNextBdSeq(Identity("B1")).Value.Should().Be(0);
        store.ReserveNextBdSeq(Identity("b1")).Value.Should().Be(1);
    }

    [Fact]
    public void Endpoint_OmittedVsExplicitDefaultPort_IsSameIdentity()
    {
        var store = NewStore();
        var omitted = SparkplugStoreIdentity.Create(BrokerEndpoint.Create("host", null, tls: false), Node);
        var explicitDefault = SparkplugStoreIdentity.Create(BrokerEndpoint.Create("host", 1883, tls: false), Node);
        store.ReserveNextBdSeq(omitted).Value.Should().Be(0);
        store.ReserveNextBdSeq(explicitDefault).Value.Should().Be(1);
    }

    [Fact]
    public void Endpoint_TlsVsPlain_AreDistinctIdentities()
    {
        var store = NewStore();
        var plain = SparkplugStoreIdentity.Create(BrokerEndpoint.Create("h", null, tls: false), Node);
        var tls = SparkplugStoreIdentity.Create(BrokerEndpoint.Create("h", null, tls: true), Node);
        store.ReserveNextBdSeq(plain).Value.Should().Be(0);
        store.ReserveNextBdSeq(tls).Value.Should().Be(0);
    }

    [Fact]
    public void Endpoint_DifferentPorts_AreDistinctIdentities()
    {
        var store = NewStore();
        store.ReserveNextBdSeq(Identity("h", port: 1883)).Value.Should().Be(0);
        store.ReserveNextBdSeq(Identity("h", port: 1884)).Value.Should().Be(0);
    }

    [Fact]
    public void Endpoint_EquivalentForms_ContinueSameStateAfterReopen()
    {
        NewStore().ReserveNextBdSeq(Identity("B1"));
        NewStore().ResolveAliases(Identity("B1"), new[] { Key("srcA") });
        NewStore().ReserveNextBdSeq(Identity("b1")).Value.Should().Be(1);
        NewStore().ResolveAliases(Identity("b1"), new[] { Key("srcA") })[Key("srcA")].Should().Be(1);
    }

    // ==== Helpers ====

    private Action Constructing() => () => { using var _ = NewStore(); };

    private static SparkplugStoreIdentity Identity(string host, int? port = 1883, bool tls = false, string node = "edgeNode1") =>
        SparkplugStoreIdentity.Create(BrokerEndpoint.Create(host, port, tls), SparkplugEdgeNodeIdentity.Create("groupA", node));

    private static SparkplugAliasKey Key(string source, string device = "dev", string tagPath = "temp") =>
        SparkplugAliasKey.Create(source, device, tagPath);

    private static string AliasTableSql(bool withUnique, bool withPk)
    {
        var cols = "broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, " +
                   "node TEXT NOT NULL COLLATE BINARY, source TEXT NOT NULL COLLATE BINARY, " +
                   "device TEXT NOT NULL COLLATE BINARY, tagpath TEXT NOT NULL COLLATE BINARY, alias INTEGER NOT NULL";
        var pk = withPk ? ", PRIMARY KEY (broker, grp, node, source, device, tagpath)" : string.Empty;
        var unique = withUnique ? ", UNIQUE (broker, grp, node, alias)" : string.Empty;
        return $"CREATE TABLE alias ({cols}{pk}{unique});";
    }

    // Recreate bdseq WITHOUT the CHECK (columns + PK preserved) and inject a value. The
    // counter column type controls affinity: INTEGER coerces numeric strings, BLOB keeps
    // them as their literal storage class (used to inject a TEXT/REAL counter).
    private void SetBdSeqRaw(object value, string counterType = "INTEGER")
    {
        SqliteConnection.ClearAllPools();
        using var conn = new SqliteConnection(ConnString);
        conn.Open();
        Exec(conn, "DROP TABLE bdseq;");
        Exec(conn,
            "CREATE TABLE bdseq (broker TEXT NOT NULL COLLATE BINARY, grp TEXT NOT NULL COLLATE BINARY, " +
            $"node TEXT NOT NULL COLLATE BINARY, last_counter {counterType} NOT NULL, PRIMARY KEY (broker, grp, node));");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO bdseq VALUES ('{BrokerKey}','groupA','edgeNode1',$v);";
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    // Recreate alias WITHOUT the CHECK and inject a single srcA row.
    private void SetAliasRaw(object aliasValue, bool keepUnique)
    {
        SqliteConnection.ClearAllPools();
        using var conn = new SqliteConnection(ConnString);
        conn.Open();
        Exec(conn, "DROP TABLE alias;");
        Exec(conn, AliasTableSql(withUnique: keepUnique, withPk: true));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO alias VALUES ('{BrokerKey}','groupA','edgeNode1','srcA','dev','temp',$a);";
        cmd.Parameters.AddWithValue("$a", aliasValue);
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private void RawExec(string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var conn = new SqliteConnection(ConnString);
        conn.Open();
        Exec(conn, sql);
    }

    private object? RawScalar(string sql)
    {
        using var conn = new SqliteConnection(ConnString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private bool TableExistsRaw(string table) =>
        RawScalar($"SELECT 1 FROM sqlite_master WHERE type='table' AND name='{table}';") is not null;

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
