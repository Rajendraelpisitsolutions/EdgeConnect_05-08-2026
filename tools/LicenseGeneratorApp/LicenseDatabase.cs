using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ElpisEdgeConnect.LicenseGeneratorApp;

/// <summary>
/// A single generated-license record — everything the form captured plus the
/// signed artifact and an audit stamp. One instance == one row in
/// <c>IssuedLicenses</c>.
/// </summary>
public sealed class IssuedLicenseRecord
{
    public string LicenseId { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string? CompanyName { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public string LicenseType { get; init; } = "";
    public string GatewayId { get; init; } = "";
    public string IssuedAt { get; init; } = "";
    public string ExpiresAt { get; init; } = "";
    public string? Sources { get; init; }
    public string? Destinations { get; init; }
    public string? ModuleKeys { get; init; }
    public int MaxSources { get; init; }
    public int MaxSinks { get; init; }
    public int MaxRoutes { get; init; }
    public string Signature { get; init; } = "";
    public string LicenseJson { get; init; } = "";
    public string? SavedFilePath { get; init; }

    /// <summary>Audit stamp (set on load; ignored on Save, which stamps its own).</summary>
    public string? CreatedAtUtc { get; init; }

    /// <summary>Who generated it, <c>user@machine</c> (set on load).</summary>
    public string? CreatedByUser { get; init; }
}

/// <summary>
/// Local SQLite store for the License Generator. Records every license the tool
/// issues so we keep an internal customer/license history. Self-contained: the
/// DB file and schema are created on first use.
/// </summary>
public static class LicenseDatabase
{
    /// <summary>Fixed location of the license history database (owner-specified).</summary>
    public const string DatabasePath = @"D:\Work Backup\LicenseGeneratorApp\edgeconnectlicense.db";

    private static string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();

    /// <summary>
    /// Create the database file and <c>IssuedLicenses</c> table if they don't
    /// exist yet. Safe to call on every launch.
    /// </summary>
    public static void EnsureCreated()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS IssuedLicenses (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    LicenseId     TEXT    NOT NULL,
    ProductName   TEXT    NOT NULL,
    CustomerName  TEXT    NOT NULL,
    CompanyName   TEXT,
    ContactEmail  TEXT,
    ContactPhone  TEXT,
    LicenseType   TEXT    NOT NULL,
    GatewayId     TEXT    NOT NULL,
    IssuedAt      TEXT    NOT NULL,
    ExpiresAt     TEXT    NOT NULL,
    Sources       TEXT,
    Destinations  TEXT,
    ModuleKeys    TEXT,
    MaxSources    INTEGER NOT NULL,
    MaxSinks      INTEGER NOT NULL,
    MaxRoutes     INTEGER NOT NULL,
    Signature     TEXT    NOT NULL,
    LicenseJson   TEXT    NOT NULL,
    SavedFilePath TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    CreatedByUser TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_IssuedLicenses_LicenseId ON IssuedLicenses (LicenseId);
CREATE INDEX IF NOT EXISTS IX_IssuedLicenses_Customer  ON IssuedLicenses (CustomerName);
CREATE INDEX IF NOT EXISTS IX_IssuedLicenses_Gateway   ON IssuedLicenses (GatewayId);
CREATE INDEX IF NOT EXISTS IX_IssuedLicenses_ExpiresAt ON IssuedLicenses (ExpiresAt);
";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Insert (or replace, if the same <c>LicenseId</c> is re-generated) one
    /// issued-license record.
    /// </summary>
    public static void Save(IssuedLicenseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        EnsureCreated();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO IssuedLicenses
    (LicenseId, ProductName, CustomerName, CompanyName, ContactEmail, ContactPhone,
     LicenseType, GatewayId, IssuedAt, ExpiresAt, Sources, Destinations, ModuleKeys,
     MaxSources, MaxSinks, MaxRoutes, Signature, LicenseJson, SavedFilePath,
     CreatedAtUtc, CreatedByUser)
VALUES
    ($LicenseId, $ProductName, $CustomerName, $CompanyName, $ContactEmail, $ContactPhone,
     $LicenseType, $GatewayId, $IssuedAt, $ExpiresAt, $Sources, $Destinations, $ModuleKeys,
     $MaxSources, $MaxSinks, $MaxRoutes, $Signature, $LicenseJson, $SavedFilePath,
     $CreatedAtUtc, $CreatedByUser);";

        void P(string name, object? value) => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        P("$LicenseId", record.LicenseId);
        P("$ProductName", record.ProductName);
        P("$CustomerName", record.CustomerName);
        P("$CompanyName", NullIfBlank(record.CompanyName));
        P("$ContactEmail", NullIfBlank(record.ContactEmail));
        P("$ContactPhone", NullIfBlank(record.ContactPhone));
        P("$LicenseType", record.LicenseType);
        P("$GatewayId", record.GatewayId);
        P("$IssuedAt", record.IssuedAt);
        P("$ExpiresAt", record.ExpiresAt);
        P("$Sources", NullIfBlank(record.Sources));
        P("$Destinations", NullIfBlank(record.Destinations));
        P("$ModuleKeys", NullIfBlank(record.ModuleKeys));
        P("$MaxSources", record.MaxSources);
        P("$MaxSinks", record.MaxSinks);
        P("$MaxRoutes", record.MaxRoutes);
        P("$Signature", record.Signature);
        P("$LicenseJson", record.LicenseJson);
        P("$SavedFilePath", NullIfBlank(record.SavedFilePath));
        P("$CreatedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        P("$CreatedByUser", $"{Environment.UserName}@{Environment.MachineName}");

        cmd.ExecuteNonQuery();
    }

    private static object? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---- Load-mode drill-down queries (Product → Type → Company → Customer → record) ----

    /// <summary>Distinct product names that have at least one issued license.</summary>
    public static List<string> GetProducts() => QueryColumn(
        "SELECT DISTINCT ProductName FROM IssuedLicenses WHERE IFNULL(ProductName,'')<>'' ORDER BY ProductName",
        _ => { });

    /// <summary>Distinct license types (editions) issued for a product.</summary>
    public static List<string> GetLicenseTypes(string product) => QueryColumn(
        "SELECT DISTINCT LicenseType FROM IssuedLicenses WHERE ProductName=$p AND IFNULL(LicenseType,'')<>'' ORDER BY LicenseType",
        cmd => cmd.Parameters.AddWithValue("$p", product));

    /// <summary>
    /// Distinct company names for a product+type. A blank company is returned as an
    /// empty string (the caller shows it as "(none)"). Names are trimmed and
    /// de-duplicated case-insensitively so variants like "Sony", "Sony " and "sony"
    /// collapse to a single dropdown entry.
    /// </summary>
    public static List<string> GetCompanies(string product, string licenseType)
    {
        var raw = QueryColumn(
            "SELECT DISTINCT TRIM(IFNULL(CompanyName,'')) FROM IssuedLicenses " +
            "WHERE ProductName=$p AND LicenseType=$t ORDER BY 1",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$p", product);
                cmd.Parameters.AddWithValue("$t", licenseType);
            });

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var company in raw)
        {
            if (seen.Add(company))
            {
                result.Add(company);
            }
        }
        return result;
    }

    /// <summary>Distinct customer names for a product+type+company (blank company = empty string).</summary>
    public static List<string> GetCustomers(string product, string licenseType, string company) => QueryColumn(
        "SELECT DISTINCT CustomerName FROM IssuedLicenses " +
        "WHERE ProductName=$p AND LicenseType=$t AND TRIM(IFNULL(CompanyName,''))=$c COLLATE NOCASE " +
        "AND IFNULL(CustomerName,'')<>'' ORDER BY CustomerName",
        cmd =>
        {
            cmd.Parameters.AddWithValue("$p", product);
            cmd.Parameters.AddWithValue("$t", licenseType);
            cmd.Parameters.AddWithValue("$c", (company ?? string.Empty).Trim());
        });

    /// <summary>
    /// The most recently issued license matching the full drill-down selection, or
    /// <c>null</c> if none. When several licenses share the same
    /// product/type/company/customer, the latest (by <c>CreatedAtUtc</c>) wins.
    /// </summary>
    public static IssuedLicenseRecord? GetLatestLicense(
        string product, string licenseType, string company, string customer)
    {
        EnsureCreated();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT LicenseId, ProductName, CustomerName, CompanyName, ContactEmail, ContactPhone,
       LicenseType, GatewayId, IssuedAt, ExpiresAt, Sources, Destinations, ModuleKeys,
       MaxSources, MaxSinks, MaxRoutes, Signature, LicenseJson, SavedFilePath,
       CreatedAtUtc, CreatedByUser
FROM IssuedLicenses
WHERE ProductName=$p AND LicenseType=$t AND TRIM(IFNULL(CompanyName,''))=$c COLLATE NOCASE AND CustomerName=$cust
ORDER BY CreatedAtUtc DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", product);
        cmd.Parameters.AddWithValue("$t", licenseType);
        cmd.Parameters.AddWithValue("$c", (company ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$cust", customer);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        int N(int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);

        return new IssuedLicenseRecord
        {
            LicenseId = S(0) ?? "",
            ProductName = S(1) ?? "",
            CustomerName = S(2) ?? "",
            CompanyName = S(3),
            ContactEmail = S(4),
            ContactPhone = S(5),
            LicenseType = S(6) ?? "",
            GatewayId = S(7) ?? "",
            IssuedAt = S(8) ?? "",
            ExpiresAt = S(9) ?? "",
            Sources = S(10),
            Destinations = S(11),
            ModuleKeys = S(12),
            MaxSources = N(13),
            MaxSinks = N(14),
            MaxRoutes = N(15),
            Signature = S(16) ?? "",
            LicenseJson = S(17) ?? "",
            SavedFilePath = S(18),
            CreatedAtUtc = S(19),
            CreatedByUser = S(20),
        };
    }

    /// <summary>Run a single-column string query, returning the values in order.</summary>
    private static List<string> QueryColumn(string sql, Action<SqliteCommand> bind)
    {
        EnsureCreated();

        var list = new List<string>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }
        return list;
    }
}
