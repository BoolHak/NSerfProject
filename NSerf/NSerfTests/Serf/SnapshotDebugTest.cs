using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSerf.Memberlist.Configuration;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.Serf;

public class SnapshotDebugTest : IDisposable
{
    private readonly string _snapshotPath;

    public SnapshotDebugTest()
    {
        _snapshotPath = Path.Combine(Path.GetTempPath(), $"serf_debug_{Guid.NewGuid()}.snapshot");
        Console.WriteLine($"[DEBUG TEST] Using snapshot path: {_snapshotPath}");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_snapshotPath))
            {
                File.Delete(_snapshotPath);
            }
        }
        catch
        {
            // Ignore
        }
    }

    [Fact]
    public async Task Debug_SnapshotShouldContainJoinedNode()
    {
        Console.WriteLine("[DEBUG TEST] === Starting debug test ===");

        // Create two nodes
        var config1 = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        var config2 = new Config
        {
            NodeName = "node2",
            SnapshotPath = _snapshotPath,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node2",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        Console.WriteLine("[DEBUG TEST] Creating node1...");
        using var s1 = await NSerf.Serf.Serf.CreateAsync(config1);
        Console.WriteLine($"[DEBUG TEST] node1 created on port {config1.MemberlistConfig.BindPort}");

        Console.WriteLine("[DEBUG TEST] Creating node2 with snapshot...");
        var s2 = await NSerf.Serf.Serf.CreateAsync(config2);
        var s2Port = config2.MemberlistConfig.BindPort;
        Console.WriteLine($"[DEBUG TEST] node2 created on port {s2Port}");

        await Task.Delay(200);

        // Join
        Console.WriteLine($"[DEBUG TEST] node1 joining node2...");
        var joinAddr = $"127.0.0.1:{s2Port}";
        var joined = await s1.JoinAsync(new[] { joinAddr }, ignoreOld: false);
        Console.WriteLine($"[DEBUG TEST] Join result: {joined}");

        await Task.Delay(500);

        Console.WriteLine($"[DEBUG TEST] node1 members: {s1.NumMembers()}");
        Console.WriteLine($"[DEBUG TEST] node2 members: {s2.NumMembers()}");

        // Wait for snapshot flush
        Console.WriteLine("[DEBUG TEST] Waiting 2s for snapshot flush...");
        await Task.Delay(2000);

        // Check snapshot file BEFORE shutdown
        Console.WriteLine("[DEBUG TEST] Checking snapshot file BEFORE shutdown...");
        if (File.Exists(_snapshotPath))
        {
            var content = await ReadSnapshotWithRetryAsync(_snapshotPath);
            Console.WriteLine($"[DEBUG TEST] Snapshot exists, size: {content.Length} bytes");
            Console.WriteLine($"[DEBUG TEST] Snapshot content:\n{content}");
        }
        else
        {
            Console.WriteLine("[DEBUG TEST] Snapshot file DOES NOT EXIST before shutdown!");
        }

        // Shutdown node2
        Console.WriteLine("[DEBUG TEST] Shutting down node2...");
        await s2.ShutdownAsync();
        s2.Dispose();
        Console.WriteLine("[DEBUG TEST] node2 shutdown complete");

        // Check snapshot file AFTER shutdown
        Console.WriteLine("[DEBUG TEST] Checking snapshot file AFTER shutdown...");
        if (File.Exists(_snapshotPath))
        {
            var content = await File.ReadAllTextAsync(_snapshotPath);
            Console.WriteLine($"[DEBUG TEST] Snapshot exists, size: {content.Length} bytes");
            Console.WriteLine($"[DEBUG TEST] Snapshot content:\n{content}");

            // Assert
            content.Should().Contain("node1", "snapshot should contain node1");
            content.Should().Contain("alive:", "snapshot should have alive marker");
        }
        else
        {
            Console.WriteLine("[DEBUG TEST] Snapshot file DOES NOT EXIST after shutdown!");
            Assert.Fail("Snapshot file should exist after shutdown");
        }

        // NOW RESTART node2 and test auto-rejoin
        Console.WriteLine("[DEBUG TEST] === Testing RESTART and AUTO-REJOIN ===");
        Console.WriteLine($"[DEBUG TEST] Restarting node2 with same snapshot path and port {s2Port}...");
        
        config2.MemberlistConfig.BindPort = s2Port; // Reuse same port
        var s2Restarted = await NSerf.Serf.Serf.CreateAsync(config2);
        Console.WriteLine("[DEBUG TEST] node2 restarted");

        // Wait for auto-rejoin
        Console.WriteLine("[DEBUG TEST] Waiting for auto-rejoin...");
        await Task.Delay(2000);

        Console.WriteLine($"[DEBUG TEST] node1 members after restart: {s1.NumMembers()}");
        Console.WriteLine($"[DEBUG TEST] node2 members after restart: {s2Restarted.NumMembers()}");

        // Assert auto-rejoin worked
        s2Restarted.NumMembers().Should().Be(2, "node2 should have auto-rejoined");
        s1.NumMembers().Should().Be(2, "node1 should see node2 as alive again");

        await s1.ShutdownAsync();
        await s2Restarted.ShutdownAsync();
        Console.WriteLine("[DEBUG TEST] === Test complete ===");
    }

    private static async Task<string> ReadSnapshotWithRetryAsync(string path, int maxRetries = 10)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return await reader.ReadToEndAsync();
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                await Task.Delay(100);
            }
        }
        throw new IOException($"Could not read snapshot file after {maxRetries} attempts");
    }
}
