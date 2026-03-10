using RdpShield.Core.Models;
using RdpShield.Infrastructure.Sqlite;
using Xunit;

namespace RdpShield.Tests.Sqlite;

public class SqliteStoresTests
{
    [Fact]
    public async Task Can_insert_and_read_allowlist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpshield-{Guid.NewGuid():N}.db");

        try
        {
            var db = new SqliteDb(path);
            await db.InitializeAsync();

            var store = new AllowlistStore(db);

            await store.AddOrUpdateAsync("1.2.3.4", "test");
            var allowed = await store.IsAllowedAsync("1.2.3.4");
            Assert.True(allowed);

            var all = await store.GetAllAsync();
            Assert.Contains(all, x => x.Entry == "1.2.3.4" && x.Comment == "test");

            await store.RemoveAsync("1.2.3.4");
            allowed = await store.IsAllowedAsync("1.2.3.4");
            Assert.False(allowed);
        }
        finally
        {
            TestDbCleanup.Cleanup(path);
        }
    }

    [Fact]
    public async Task Can_upsert_and_read_active_bans()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpshield-{Guid.NewGuid():N}.db");

        try
        {
            var db = new SqliteDb(path);
            await db.InitializeAsync();

            var store = new BanStore(db);

            var now = DateTimeOffset.Parse("2026-03-05T10:00:00Z");
            var ban = new BanRecord(
                Ip: "5.6.7.8",
                Reason: "test ban",
                Source: "RDP",
                FirstSeenUtc: now.AddMinutes(-1),
                LastSeenUtc: now,
                ExpiresUtc: now.AddMinutes(60),
                AttemptsInWindow: 3
            );

            await store.UpsertBanAsync(ban);

            var active = await store.GetActiveBansAsync();
            Assert.Single(active);
            Assert.Equal("5.6.7.8", active[0].Ip);

            // Upsert again (change expires/attempts)
            var ban2 = ban with
            {
                ExpiresUtc = now.AddMinutes(120),
                AttemptsInWindow = 7
            };

            await store.UpsertBanAsync(ban2);

            var loaded = await store.GetBanAsync("5.6.7.8");
            Assert.NotNull(loaded);
            Assert.Equal(7, loaded!.AttemptsInWindow);
            Assert.Equal(now.AddMinutes(120), loaded.ExpiresUtc);
        }
        finally
        {
            TestDbCleanup.Cleanup(path);
        }
    }

    [Fact]
    public async Task Can_find_expired_bans_and_mark_unbanned()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpshield-{Guid.NewGuid():N}.db");

        try
        {
            var db = new SqliteDb(path);
            await db.InitializeAsync();

            var store = new BanStore(db);

            var now = DateTimeOffset.Parse("2026-03-05T10:00:00Z");

            var expired = new BanRecord(
                Ip: "10.0.0.1",
                Reason: "expired",
                Source: "RDP",
                FirstSeenUtc: now.AddMinutes(-10),
                LastSeenUtc: now.AddMinutes(-9),
                ExpiresUtc: now.AddMinutes(-1),
                AttemptsInWindow: 3
            );

            var active = new BanRecord(
                Ip: "10.0.0.2",
                Reason: "active",
                Source: "RDP",
                FirstSeenUtc: now.AddMinutes(-10),
                LastSeenUtc: now.AddMinutes(-9),
                ExpiresUtc: now.AddMinutes(30),
                AttemptsInWindow: 3
            );

            await store.UpsertBanAsync(expired);
            await store.UpsertBanAsync(active);

            var expiredList = await store.GetExpiredActiveBansAsync(now);
            Assert.Single(expiredList);
            Assert.Equal("10.0.0.1", expiredList[0].Ip);
            Assert.True(await store.IsActiveBanAsync("10.0.0.1"));

            await store.MarkUnbannedAsync("10.0.0.1", now);
            Assert.False(await store.IsActiveBanAsync("10.0.0.1"));

            var activeList = await store.GetActiveBansAsync();
            Assert.Single(activeList);
            Assert.Equal("10.0.0.2", activeList[0].Ip);
        }
        finally
        {
            TestDbCleanup.Cleanup(path);
        }
    }

    [Fact]
    public async Task Can_append_and_read_latest_events()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpshield-{Guid.NewGuid():N}.db");

        try
        {
            var db = new SqliteDb(path);
            await db.InitializeAsync();

            var store = new EventStore(db);

            var t1 = DateTimeOffset.Parse("2026-03-05T10:00:00Z");
            var t2 = t1.AddSeconds(10);

            await store.AppendAsync(t1, "Information", "ServiceStarted", "Service is up");
            await store.AppendAsync(t2, "Warning", "IpBanned", "Banned IP", ip: "1.1.1.1", source: "RDP");

            var latest = await store.GetLatestAsync(10);
            Assert.True(latest.Count >= 2);

            // Latest should be the last inserted (order by id desc)
            var first = latest[0];
            Assert.Equal("Warning", first.level);
            Assert.Equal("IpBanned", first.type);
            Assert.Equal("1.1.1.1", first.ip);
        }
        finally
        {
            TestDbCleanup.Cleanup(path);
        }
    }

    [Fact]
    public async Task Can_page_latest_events_with_skip_without_overlap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpshield-{Guid.NewGuid():N}.db");

        try
        {
            var db = new SqliteDb(path);
            await db.InitializeAsync();

            var store = new EventStore(db);
            var t0 = DateTimeOffset.Parse("2026-03-05T10:00:00Z");

            for (var i = 1; i <= 5; i++)
            {
                await store.AppendAsync(
                    t0.AddSeconds(i),
                    "Information",
                    $"E{i}",
                    $"Event {i}");
            }

            var page1 = await store.GetLatestAsync(2, skip: 0);
            var page2 = await store.GetLatestAsync(2, skip: 2);

            Assert.Equal(2, page1.Count);
            Assert.Equal(2, page2.Count);

            Assert.Equal("E5", page1[0].type);
            Assert.Equal("E4", page1[1].type);
            Assert.Equal("E3", page2[0].type);
            Assert.Equal("E2", page2[1].type);

            var ids1 = page1.Select(x => x.type).ToHashSet(StringComparer.Ordinal);
            var ids2 = page2.Select(x => x.type).ToHashSet(StringComparer.Ordinal);
            Assert.Empty(ids1.Intersect(ids2));
        }
        finally
        {
            TestDbCleanup.Cleanup(path);
        }
    }

    [Fact]
    public async Task Dashboard_stats_count_failed_attempt_events()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpshield-{Guid.NewGuid():N}.db");

        try
        {
            var db = new SqliteDb(path);
            await db.InitializeAsync();

            var events = new EventStore(db);
            var stats = new StatsServiceSqlite(db);

            var now = DateTimeOffset.UtcNow;
            await events.AppendAsync(now.AddMinutes(-5), "Information", "AuthFailedDetected", "Failed auth from 1.2.3.4", ip: "1.2.3.4", source: "RDP");
            await events.AppendAsync(now.AddMinutes(-2), "Warning", "IpBanned", "Banned 1.2.3.4", ip: "1.2.3.4", source: "RDP");

            var dto = await stats.GetDashboardStatsAsync();

            Assert.Equal(1, dto.FailedAttemptsLast10m);
            Assert.Equal("1.2.3.4", dto.LastBannedIp);
            Assert.NotNull(dto.LastBannedAtUtc);
        }
        finally
        {
            TestDbCleanup.Cleanup(path);
        }
    }
}
