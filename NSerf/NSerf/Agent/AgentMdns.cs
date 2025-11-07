// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using MdnsServiceDiscovery = Makaretu.Dns.ServiceDiscovery;

namespace NSerf.Agent;

/// <summary>
/// AgentMdns is used to advertise ourselves using mDNS and to
/// attempt to join peers periodically using mDNS queries.
/// Maps to: Go's mdns.go
/// </summary>
public sealed class AgentMdns : IDisposable
{
    private const int MdnsPollInterval = 60; // seconds
    private const int MdnsQuietInterval = 100; // milliseconds

    private static readonly Lazy<MulticastService> SharedMdns = new(() =>
    {
        var mdns = new MulticastService();
        mdns.Start();
        return mdns;
    });

    private readonly SerfAgent _agent;
    private readonly string _discover;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private readonly MdnsServiceDiscovery _serviceDiscovery;
    private readonly ServiceProfile _serviceProfile;
    private readonly bool _replay;
    private readonly bool _disableIPv4;
    private readonly bool _disableIPv6;
    private readonly string _ourAddress;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _runTask;
    private bool _disposed;

    /// <summary>
    /// Creates a new AgentMdns instance.
    /// </summary>
    /// <param name="agent">The Serf agent to join discovered peers to</param>
    /// <param name="replay">Whether to replay user events on join</param>
    /// <param name="node">The node name to advertise</param>
    /// <param name="discover">The cluster name for discovery</param>
    /// <param name="bind">IP address to bind to</param>
    /// <param name="port">Port number to advertise</param>
    /// <param name="disableIPv4">Disable IPv4 support</param>
    /// <param name="disableIPv6">Disable IPv6 support</param>
    /// <param name="logger">Logger instance</param>
    public AgentMdns(
        SerfAgent agent,
        bool replay,
        string node,
        string discover,
        IPAddress bind,
        int port,
        bool disableIPv4 = false,
        bool disableIPv6 = false,
        ILogger? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _logger = logger;
        _replay = replay;
        _disableIPv4 = disableIPv4;
        _disableIPv6 = disableIPv6;
        _ourAddress = $"{bind}:{port}";

        // Create the service profile
        var serviceName = MdnsName(discover);
        _serviceProfile = new ServiceProfile(
            instanceName: node,
            serviceName: serviceName,
            port: (ushort)port,
            addresses: [bind]
        );

        // Add TXT record with cluster info
        _serviceProfile.AddProperty("cluster", $"Serf '{discover}' cluster");

        // Use shared multicast service and create service discovery
        var mdns = SharedMdns.Value;
        _serviceDiscovery = new MdnsServiceDiscovery(mdns);

        // Start advertising
        _serviceDiscovery.Advertise(_serviceProfile);
        
        _logger?.LogInformation("[mDNS] Advertising service: {ServiceName} on {Bind}:{Port}", serviceName, bind, port);

        // Start the background worker
        _runTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);

        _logger?.LogInformation("[mDNS] Started discovery for cluster: {Discover}", discover);
    }

    /// <summary>
    /// Background worker that scans for new hosts periodically.
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var hosts = new ConcurrentQueue<ServiceInstanceDiscoveryEventArgs>();
        Timer? quietTimer = null;
        var joinList = new List<string>();
        var joinLock = new object();

        // Subscribe to service instance discovered events
        EventHandler<ServiceInstanceDiscoveryEventArgs> discoveryHandler = (_, e) =>
        {
            _logger?.LogDebug("[mDNS] Service instance discovered: {ServiceName}", e.ServiceInstanceName);
            hosts.Enqueue(e);
        };
        
        _serviceDiscovery.ServiceInstanceDiscovered += discoveryHandler;

        // Initial poll
        _ = Task.Run(() => Poll(cancellationToken), cancellationToken);

        var lastPollTime = DateTime.UtcNow;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Process discovered hosts
                while (hosts.TryDequeue(out var host))
                {
                    var addr = FormatAddress(host);
                    if (addr == null || addr == _ourAddress || _seen.ContainsKey(addr)) continue;

                    _logger?.LogDebug("[mDNS] Queueing host for join: {Address}", addr);

                    // Queue for handling
                    lock (joinLock)
                    {
                        joinList.Add(addr);
                    }

                    // Reset quiet timer
                    if(quietTimer != null) await quietTimer.DisposeAsync();
                    quietTimer = new Timer(async void (_) =>
                    {
                        try
                        {
                            List<string> toJoin;
                            lock (joinLock)
                            {
                                toJoin = new List<string>(joinList);
                                joinList.Clear();
                            }

                            if (toJoin.Count <= 0) return;
                            _logger?.LogDebug("[mDNS] Attempting to join {Count} hosts after quiet interval", toJoin.Count);
                            await AttemptJoinAsync(toJoin);
                        }
                        catch (Exception e)
                        {
                            _logger?.LogError(e,"[mDNS] error in timer: {Error}", e.Message);
                        }
                    }, null, MdnsQuietInterval, Timeout.Infinite);
                }

                // Check if it's time to poll again (every 60 seconds)
                var now = DateTime.UtcNow;
                if ((now - lastPollTime).TotalSeconds >= MdnsPollInterval)
                {
                    lastPollTime = now;
                    _ = Task.Run(() => Poll(cancellationToken), cancellationToken);
                }

                // Short delay before next iteration
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        finally
        {
            if(quietTimer != null ) await quietTimer.DisposeAsync();
            _serviceDiscovery.ServiceInstanceDiscovered -= discoveryHandler;
        }
    }

    /// <summary>
    /// Polls for new hosts using mDNS query.
    /// </summary>
    private void Poll(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            var serviceName = MdnsName(_discover);
            _serviceDiscovery.QueryServiceInstances(serviceName);
            _logger?.LogDebug("[mDNS] Polling for service: {Service}", serviceName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[mDNS] Failed to poll for new hosts");
        }
    }

    /// <summary>
    /// Attempts to join the discovered hosts.
    /// </summary>
    private async Task AttemptJoinAsync(List<string> hosts)
    {
        try
        {
            _logger?.LogInformation("[mDNS] Attempting to join {Count} hosts: {Hosts}", hosts.Count, string.Join(", ", hosts));
            var joined = await _agent.Serf!.JoinAsync(hosts.ToArray(), !_replay);
            _logger?.LogInformation("[mDNS] Successfully joined {Joined}/{Total} hosts", joined, hosts.Count);

            // Mark all as seen
            foreach (var host in hosts)
            {
                _seen.TryAdd(host, 0);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[mDNS] Failed to join hosts: {Hosts}", string.Join(", ", hosts));
        }
    }

    /// <summary>
    /// Formats a service instance address as "IP:Port".
    /// </summary>
    private string? FormatAddress(ServiceInstanceDiscoveryEventArgs args)
    {
        try
        {
            // Get the first A or AAAA record from Answers or AdditionalRecords
            var addressRecord = args.Message.Answers
                .OfType<AddressRecord>()
                .FirstOrDefault();

            if (addressRecord == null)
            {
                addressRecord = args.Message.AdditionalRecords
                    .OfType<AddressRecord>()
                    .FirstOrDefault();
            }

            if (CanNotBeFormatted(addressRecord)) return null;

            // Get the SRV record for port from Answers or AdditionalRecords
            var srvRecord = args.Message.Answers
                .OfType<SRVRecord>()
                .FirstOrDefault();

            if (srvRecord == null)
            {
                srvRecord = args.Message.AdditionalRecords
                    .OfType<SRVRecord>()
                    .FirstOrDefault();
            }

            if (srvRecord == null) return null;

            var result = $"{addressRecord!.Address}:{srvRecord.Port}";
            _logger?.LogDebug("[mDNS] Discovered peer at {Address}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[mDNS] Failed to format address");
            return null;
        }
    }

    private bool CanNotBeFormatted(AddressRecord? addressRecord) =>
        addressRecord == null  
        || (_disableIPv4 && addressRecord.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) 
        || (_disableIPv6 && addressRecord.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);

    /// <summary>
    /// Returns the mDNS service name to register and lookup.
    /// </summary>
    private static string MdnsName(string discover) => $"_serf_{discover}._tcp";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cts.Cancel();

        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore cleanup errors
        }

        _serviceDiscovery.Unadvertise(_serviceProfile);
        _serviceDiscovery.Dispose();
        _cts.Dispose();

        _logger?.LogInformation("[mDNS] Stopped discovery");
    }
}
