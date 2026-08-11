// ============================================================================
// File: Program.cs
// Purpose: Application entry point and DI composition root.
//          Supports three data source types: MT-LINKi REST API, FOCAS2 DLL,
//          and HTTP REST (simulator). Each machine can use a different source.
//
// CLI MODE (secret management):
//   dotnet run -- generate-key           → Generate AES-256 encryption key
//   dotnet run -- encrypt "password"     → Encrypt a password for config
//
// STARTUP ORDER:
//   1. Configuration loaded (appsettings.json + env + CLI)
//   2. Serilog logging initialized
//   3. DI container built
//   4. MqttPublisherService starts → connects to broker
//   5. MachineManagerService starts → creates pollers with correct data sources
// ============================================================================

using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.DataSources;
using ElpisEdgeConnect.Security;
using ElpisEdgeConnect.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// ── CLI Mode: Secret management commands ──
if (args.Length > 0)
{
    if (args[0].Equals("generate-key", StringComparison.OrdinalIgnoreCase))
    {
        var key = SecretProvider.GenerateKey();
        Console.WriteLine("========================================");
        Console.WriteLine("  New AES-256 Encryption Key Generated");
        Console.WriteLine("========================================");
        Console.WriteLine($"\n  Key: {key}\n");
        Console.WriteLine("  Set as environment variable:");
        Console.WriteLine($"    export EDGECONNECT_ENCRYPTION_KEY=\"{key}\"");
        Console.WriteLine("\n  NEVER store this key in config files.");
        Console.WriteLine("========================================");
        return;
    }

    if (args[0].Equals("encrypt", StringComparison.OrdinalIgnoreCase) && args.Length > 1)
    {
        var keyBase64 = Environment.GetEnvironmentVariable("EDGECONNECT_ENCRYPTION_KEY");
        if (string.IsNullOrEmpty(keyBase64))
        {
            Console.Error.WriteLine("ERROR: EDGECONNECT_ENCRYPTION_KEY not set.");
            Environment.Exit(1);
            return;
        }

        var key = Convert.FromBase64String(keyBase64);
        var encrypted = SecretProvider.Encrypt(args[1], key);
        Console.WriteLine("========================================");
        Console.WriteLine("  Password Encrypted Successfully");
        Console.WriteLine("========================================");
        Console.WriteLine($"\n  Encrypted: {encrypted}");
        Console.WriteLine($"\n  Paste into appsettings.json:");
        Console.WriteLine($"    \"Password\": \"{encrypted}\"");
        Console.WriteLine("========================================");
        return;
    }
}

// ── Service Mode: Build and run the host ──

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/edgeconnect-.log",
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        retainedFileCountLimit: 31,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] " +
                        "{SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("========================================");
    Log.Information("Elpis EdgeConnect v2.0 starting...");
    Log.Information("Data Sources: MT-LINKi | FOCAS2 | MTConnect | Brother HTTP | HTTP REST");
    Log.Information("========================================");

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();

    // ── Configuration binding ──
    builder.Services.Configure<AppSettings>(
        builder.Configuration.GetSection(AppSettings.SectionName));
    builder.Services.Configure<MqttSettings>(
        builder.Configuration.GetSection(MqttSettings.SectionName));
    builder.Services.Configure<List<MachineConfig>>(
        builder.Configuration.GetSection("Machines"));

    // ── Service Registration ──

    // SecretProvider — resolves env vars and encrypted passwords
    builder.Services.AddSingleton<ISecretProvider, SecretProvider>();

    // DataSourceFactory — creates MT-LINKi/FOCAS2/HTTP data sources per machine
    // Evolved from FanucApiClientFactory — now supports all data source types
    builder.Services.AddSingleton<IDataSourceFactory, DataSourceFactory>();

    // MQTT Publisher — background service that publishes to MQTT broker
    builder.Services.AddSingleton<MqttPublisherService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisherService>());

    // Machine Manager — orchestrates all machine pollers
    builder.Services.AddHostedService<MachineManagerService>();

    var host = builder.Build();

    Log.Information("Host built — starting hosted services...");
    await host.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "Elpis EdgeConnect terminated due to a fatal error");
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("Elpis EdgeConnect shutting down");
    await Log.CloseAndFlushAsync();
}
