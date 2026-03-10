using Microsoft.Data.Sqlite;

namespace RdpShield.Tests.Sqlite;

internal static class TestDbCleanup
{
    public static void Cleanup(string path)
    {
        // Ensure any pooled connections are released
        SqliteConnection.ClearAllPools();

        DeleteIfExists(path + "-wal");
        DeleteIfExists(path + "-shm");
        DeleteIfExists(path);
    }

    private static void DeleteIfExists(string p)
    {
        if (!File.Exists(p)) return;

        // Small retry loop to avoid transient locks on Windows
        for (var i = 0; i < 5; i++)
        {
            try
            {
                File.Delete(p);
                return;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(50);
            }
        }
    }
}