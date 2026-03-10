using RdpShield.Core.Abstractions;
using RdpShield.Core.Engine;
using RdpShield.Infrastructure.Sqlite;
using RdpShield.Service;
using RdpShield.Service.Live;
using RdpShield.Service.Security;
using RdpShield.Service.Settings;
using Serilog;

static string GetDataDir(bool devLocal)
{
    if (devLocal)
    {
        var dir = Path.Combine(Environment.CurrentDirectory, ".data");
        Directory.CreateDirectory(dir);
        return dir;
    }

    var dir2 = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RdpShield");
    Directory.CreateDirectory(dir2);
    return dir2;
}

var builder = Host.CreateApplicationBuilder(args);

// DevLocal by environment (no appsettings needed)
var devLocal = builder.Environment.IsDevelopment();
var dataDir = GetDataDir(devLocal);
var dbPath = Path.Combine(dataDir, "rdpshield.db");
var settingsPath = Path.Combine(dataDir, "settings.json");

// Serilog (console + file in install dir)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "rdpshield-.log"),
        rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// Windows Service support
builder.Services.AddWindowsService(o => o.ServiceName = "RdpShield");

// Core services
builder.Services.AddSingleton<IClock, SystemClock>();

// Settings store (ProgramData/.data)
builder.Services.AddSingleton(_ =>
{
    var store = new SettingsStore(settingsPath);
    store.LoadOrCreate(); // creates settings.json with defaults if missing
    return store;
});

// SqliteDb
builder.Services.AddSingleton(_ => new SqliteDb(dbPath));

// Stores (SQLite)
builder.Services.AddSingleton<IAllowlistStore, AllowlistStore>();
builder.Services.AddSingleton<IBanStore, BanStore>();

// Event store + live hub
builder.Services.AddSingleton<EventStore>(); // concrete sqlite store
builder.Services.AddSingleton<LiveEventHub>();
builder.Services.AddSingleton<IEventStore>(sp =>
{
    var inner = sp.GetRequiredService<EventStore>();
    var hub = sp.GetRequiredService<LiveEventHub>();
    return new LiveEventStoreDecorator(inner, hub);
});

// Stats service
builder.Services.AddSingleton<IStatsService, StatsServiceSqlite>();

// Firewall provider (Windows Firewall via PowerShell)
builder.Services.AddSingleton<IFirewallProvider, RdpShield.Infrastructure.Windows.Firewall.WindowsFirewallProvider>();

// Allowlist adapter (sync for engine)
builder.Services.AddSingleton<IAllowlist, AllowlistCached>();

// Monitor (real Windows Security 4625)
builder.Services.AddSingleton<IMonitor, RdpShield.Infrastructure.Windows.Monitoring.Rdp4625Monitor>();

// Engine: rebuild logic is in Worker (based on settings store changes)
builder.Services.AddSingleton<BanEngineFactory>();

// Named pipes:
// 1) Request/response API (commands, stats, lists, settings)
builder.Services.AddHostedService<PipeServerService>();

// 2) Live event stream (push events)
builder.Services.AddHostedService<PipeEventsServerService>();

// Hosted workers
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<BanCleanupService>();

var host = builder.Build();

// Init DB before run
{
    var db = host.Services.GetRequiredService<SqliteDb>();
    await db.InitializeAsync();
}

try
{
    Log.Information("Starting host... DataDir={DataDir} DevLocal={DevLocal}", dataDir, devLocal);
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
