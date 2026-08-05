// ============================================================================
// File: Program.cs
// Purpose: CLI entrypoint for LicenseGen.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B3
//
// Subcommands:
//   keygen --out <dir>
//       Generate a new RSA-2048 keypair under <dir>/public.pem and private.pem.
//
//   new --customer <name> --gateway <id> --edition <Starter|Professional|Enterprise>
//       --expires <yyyy-MM-dd> --private-key <path> --out <path>
//       [--license-id <id>] [--module key=enabled[:max] ...]
//       [--max-sources N] [--max-sinks N] [--max-routes N]
//       Generate a signed license file.
//
// Signing uses Core's CanonicalJson + LicenseSignatureValidator so the
// signer cannot drift from the verifier.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.Tools.LicenseGen;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "keygen" => RunKeygen(args.Skip(1).ToArray()),
                "new" => RunNew(args.Skip(1).ToArray()),
                _ => Fail("Unknown command: " + args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static int RunKeygen(string[] args)
    {
        var opts = ParseArgs(args);
        var outDir = opts.GetValueOrDefault("out") ?? "keys";
        KeyGenerator.Generate(outDir);
        Console.WriteLine($"Wrote keypair to {Path.GetFullPath(outDir)}");
        return 0;
    }

    private static int RunNew(string[] args)
    {
        var opts = ParseArgs(args);

        var customer = Require(opts, "customer");
        var gatewayId = Require(opts, "gateway");
        var editionStr = Require(opts, "edition");
        var expires = Require(opts, "expires");
        var privateKeyPath = Require(opts, "private-key");
        var outPath = Require(opts, "out");
        var licenseId = opts.GetValueOrDefault("license-id") ?? $"LIC-{DateTime.UtcNow:yyyyMMddHHmmss}";

        if (!Enum.TryParse<LicenseEdition>(editionStr, ignoreCase: true, out var edition) || edition == LicenseEdition.Unknown)
        {
            return Fail($"Invalid edition: {editionStr}");
        }

        var maxSources = int.Parse(opts.GetValueOrDefault("max-sources") ?? "50", CultureInfo.InvariantCulture);
        var maxSinks = int.Parse(opts.GetValueOrDefault("max-sinks") ?? "5", CultureInfo.InvariantCulture);
        var maxRoutes = int.Parse(opts.GetValueOrDefault("max-routes") ?? "100", CultureInfo.InvariantCulture);

        var modules = ParseModuleList(opts.GetValueOrDefault("modules"));

        // Build payload as a JsonElement tree via raw JSON.
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["customer"] = customer,
            ["edition"] = edition.ToString(),
            ["expiresAt"] = expires,
            ["gatewayId"] = gatewayId,
            ["issuedAt"] = opts.GetValueOrDefault("issued") ?? DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["licenseId"] = licenseId,
            ["limits"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxRoutes"] = maxRoutes,
                ["maxSinkInstances"] = maxSinks,
                ["maxSourceInstances"] = maxSources,
            },
            ["modules"] = modules,
        };

        var unsignedJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        var canonical = CanonicalJson.Canonicalize(unsignedJson);
        var privatePem = File.ReadAllText(privateKeyPath);
        var signature = LicenseSignatureValidator.SignWithPrivateKey(canonical, privatePem);

        // Final JSON: payload + signature
        var withSig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(unsignedJson)!;
        var finalDoc = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in withSig)
        {
            finalDoc[kvp.Key] = JsonSerializer.Deserialize<object?>(kvp.Value.GetRawText());
        }
        finalDoc["signature"] = signature;

        var indented = JsonSerializer.Serialize(finalDoc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, indented, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Wrote license to {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static SortedDictionary<string, object?> ParseModuleList(string? csv)
    {
        // Format: source.focas2=true:20,source.modbus=true,sink.mqtt=true
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv))
        {
            // Default: enable everything with no per-module cap.
            foreach (var key in new[]
            {
                "source.focas2", "source.mtlinki", "source.mtconnect",
                "source.brotherhttp", "source.modbus", "sink.mqtt", "sink.http", "sink.tcp",
            })
            {
                result[key] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["enabled"] = true,
                };
            }
            return result;
        }

        foreach (var entry in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0) continue;
            var key = entry.Substring(0, eq).Trim();
            var rhs = entry.Substring(eq + 1);
            var parts = rhs.Split(':');
            var enabled = bool.Parse(parts[0]);
            var module = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["enabled"] = enabled,
            };
            if (parts.Length > 1)
            {
                module["maxInstances"] = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }
            result[key] = module;
        }
        return result;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                var key = args[i].Substring(2);
                var value = i + 1 < args.Length ? args[++i] : "";
                dict[key] = value;
            }
        }
        return dict;
    }

    private static string Require(Dictionary<string, string> opts, string key)
    {
        if (!opts.TryGetValue(key, out var v) || string.IsNullOrEmpty(v))
        {
            throw new ArgumentException($"Missing required option --{key}");
        }
        return v;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  LicenseGen keygen --out <dir>");
        Console.Error.WriteLine("  LicenseGen new --customer <name> --gateway <id> --edition <Starter|Professional|Enterprise>");
        Console.Error.WriteLine("                  --expires <yyyy-MM-dd> --private-key <path> --out <path>");
        Console.Error.WriteLine("                  [--license-id <id>] [--max-sources N] [--max-sinks N] [--max-routes N]");
        Console.Error.WriteLine("                  [--modules key=enabled[:max],...]");
    }
}
