using System.Text.Json;
using RdpShield.Api.Client;

var jsonOut = new JsonSerializerOptions { WriteIndented = true };

static void Usage()
{
    Console.WriteLine("""
Usage:
  RdpShield.Cli stats
  RdpShield.Cli bans
  RdpShield.Cli events [take]
  RdpShield.Cli unban <ip>
  RdpShield.Cli allow
  RdpShield.Cli allow-add <entry> [comment]
  RdpShield.Cli allow-del <entry>
  RdpShield.Cli tail
""");
}

if (args.Length == 0)
{
    Usage();
    return;
}

var client = new RdpShieldPipeClient();

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "stats":
        {
            var s = await client.GetDashboardStatsAsync();
            Console.WriteLine(JsonSerializer.Serialize(s, jsonOut));
            break;
        }

        case "bans":
        {
            var bans = await client.GetActiveBansAsync();
            Console.WriteLine(JsonSerializer.Serialize(bans, jsonOut));
            break;
        }

        case "events":
        {
            var take = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 20;
            var ev = await client.GetRecentEventsAsync(take);
            Console.WriteLine(JsonSerializer.Serialize(ev, jsonOut));
            break;
        }

        case "unban":
        {
            if (args.Length < 2) { Usage(); return; }
            await client.UnbanIpAsync(args[1]);
            Console.WriteLine("OK");
            break;
        }

        case "allow":
        {
            var list = await client.GetAllowlistAsync();
            Console.WriteLine(JsonSerializer.Serialize(list, jsonOut));
            break;
        }

        case "allow-add":
        {
            if (args.Length < 2) { Usage(); return; }
            var entry = args[1];
            var comment = args.Length > 2 ? args[2] : null;
            await client.AddAllowlistEntryAsync(entry, comment);
            Console.WriteLine("OK");
            break;
        }

        case "allow-del":
        {
            if (args.Length < 2) { Usage(); return; }
            await client.RemoveAllowlistEntryAsync(args[1]);
            Console.WriteLine("OK");
            break;
        }

        case "tail":
        {
            Console.WriteLine("Streaming events... Ctrl+C to stop");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var stream = new RdpShieldEventsStreamClient();
            await stream.RunAsync(evt =>
            {
                Console.WriteLine($"{evt.TsUtc:u} {evt.Level} {evt.Type} ip={evt.Ip} {evt.Message}");
                return Task.CompletedTask;
            }, cts.Token);
            break;
        }

        default:
            Usage();
            break;
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
    Environment.ExitCode = 2;
}
