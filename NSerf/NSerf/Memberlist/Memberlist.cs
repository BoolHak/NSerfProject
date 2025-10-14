// Ported from: github.com/hashicorp/memberlist/memberlist.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Memberlist manages cluster membership and member failure detection using a gossip-based protocol.
/// It is eventually consistent but converges quickly. Node failures are detected and network partitions
/// are partially tolerated by attempting to communicate with potentially dead nodes through multiple routes.
/// </summary>
public class Memberlist : IDisposable
{
    // Atomic counters
    private uint _sequenceNum;
    private uint _incarnation;
    internal uint _numNodes;
    private uint _pushPullReq;
    
    // Advertise address
    private readonly object _advertiseLock = new();
    private IPAddress _advertiseAddr = IPAddress.None;
    private ushort _advertisePort;
    
    // Configuration and lifecycle
    internal readonly MemberlistConfig _config;
    private int _shutdown; // Atomic boolean
    private readonly CancellationTokenSource _shutdownCts = new();
    private int _leave; // Atomic boolean
    
    // Transport
    private readonly INodeAwareTransport _transport;
    private readonly PacketHandler _packetHandler;
    private readonly List<Task> _backgroundTasks = new();
    
    // Node management
    internal readonly object _nodeLock = new();
    internal readonly List<NodeState> _nodes = new();
    internal readonly ConcurrentDictionary<string, NodeState> _nodeMap = new();
    internal readonly ConcurrentDictionary<string, object> _nodeTimers = new(); // Suspicion timers
    
    // Health awareness
    internal readonly Awareness _awareness;
    
    // Ack handlers
    internal readonly ConcurrentDictionary<uint, object> _ackHandlers = new(); // TODO: Replace with ackHandler type
    
    // Logging
    private readonly ILogger? _logger;
    
    // Local node cache
    private Node? _localNode;
    
    private bool _disposed;
    
    private Memberlist(MemberlistConfig config, INodeAwareTransport transport)
    {
        _config = config;
        _transport = transport;
        _logger = config.Logger;
        _incarnation = 0;
        _sequenceNum = 0;
        _numNodes = 1; // Start with just ourselves
        _awareness = new Awareness(config.AwarenessMaxMultiplier);
        _packetHandler = new PacketHandler(this, _logger);
    }
    
    /// <summary>
    /// Creates a new Memberlist using the given configuration.
    /// This will not connect to any other node yet, but will start all the listeners
    /// to allow other nodes to join this memberlist.
    /// </summary>
    public static async Task<Memberlist> CreateAsync(MemberlistConfig config, CancellationToken cancellationToken = default)
    {
        // Validate protocol version
        if (config.ProtocolVersion < ProtocolVersion.Min)
        {
            throw new ArgumentException(
                $"Protocol version '{config.ProtocolVersion}' too low. Must be in range: [{ProtocolVersion.Min}, {ProtocolVersion.Max}]",
                nameof(config));
        }
        
        if (config.ProtocolVersion > ProtocolVersion.Max)
        {
            throw new ArgumentException(
                $"Protocol version '{config.ProtocolVersion}' too high. Must be in range: [{ProtocolVersion.Min}, {ProtocolVersion.Max}]",
                nameof(config));
        }
        
        // Validate node name
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new ArgumentException("Node name is required", nameof(config));
        }
        
        // TODO: Handle secret key and keyring setup
        
        // Ensure we have a transport
        if (config.Transport == null)
        {
            throw new ArgumentException("Transport is required", nameof(config));
        }
        
        // Wrap transport if needed
        INodeAwareTransport transport = config.Transport as INodeAwareTransport ?? 
            throw new ArgumentException("Transport must implement INodeAwareTransport", nameof(config));
        
        // Create memberlist
        var memberlist = new Memberlist(config, transport);
        
        // Initialize local node
        await memberlist.InitializeLocalNodeAsync();
        
        // Start background listeners
        memberlist.StartBackgroundListeners();
        
        return memberlist;
    }
    
    /// <summary>
    /// Gets the local node information.
    /// </summary>
    public Node LocalNode
    {
        get
        {
            if (_localNode == null)
            {
                throw new InvalidOperationException("Local node not initialized");
            }
            return _localNode;
        }
    }
    
    /// <summary>
    /// Returns the number of members in the cluster from this node's perspective.
    /// </summary>
    public int NumMembers()
    {
        lock (_nodeLock)
        {
            return _nodeMap.Count;
        }
    }
    
    /// <summary>
    /// Returns the health score of the local node. Lower values are healthier.
    /// 0 is perfectly healthy.
    /// </summary>
    public int GetHealthScore()
    {
        return _awareness.GetHealthScore();
    }
    
    /// <summary>
    /// Returns the current sequence number.
    /// </summary>
    public uint SequenceNum => Interlocked.CompareExchange(ref _sequenceNum, 0, 0);
    
    /// <summary>
    /// Returns the estimated number of nodes in the cluster.
    /// </summary>
    internal int EstNumNodes()
    {
        return (int)Interlocked.CompareExchange(ref _numNodes, 0, 0);
    }
    
    /// <summary>
    /// Returns the number of push/pull requests made.
    /// </summary>
    internal uint PushPullRequests => Interlocked.CompareExchange(ref _pushPullReq, 0, 0);
    
    /// <summary>
    /// Checks if we're in the leave state.
    /// </summary>
    internal bool IsLeaving => Interlocked.CompareExchange(ref _leave, 0, 0) == 1;
    
    /// <summary>
    /// Returns the next sequence number atomically.
    /// </summary>
    public uint NextSequenceNum()
    {
        return Interlocked.Increment(ref _sequenceNum);
    }
    
    /// <summary>
    /// Returns the current incarnation number.
    /// </summary>
    public uint Incarnation => Interlocked.CompareExchange(ref _incarnation, 0, 0);
    
    /// <summary>
    /// Increments and returns the incarnation number atomically.
    /// </summary>
    public uint NextIncarnation()
    {
        return Interlocked.Increment(ref _incarnation);
    }
    
    /// <summary>
    /// Skips the incarnation number by the given offset.
    /// Returns the new incarnation number.
    /// </summary>
    internal uint SkipIncarnation(uint offset)
    {
        return Interlocked.Add(ref _incarnation, offset);
    }
    
    /// <summary>
    /// Initiates a graceful shutdown of the memberlist.
    /// </summary>
    public async Task ShutdownAsync()
    {
        // Check if already shutdown
        if (Interlocked.CompareExchange(ref _shutdown, 1, 0) == 1)
        {
            // Already shutdown
            return;
        }
        
        _logger?.LogInformation("Memberlist: Shutting down");
        
        // Signal shutdown
        _shutdownCts.Cancel();
        
        // Wait for background tasks to complete
        try
        {
            await Task.WhenAll(_backgroundTasks.ToArray());
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error waiting background tasks");
        }
        
        // Close transport
        try
        {
            await _transport.ShutdownAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error shutting down transport");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Starts background listeners for the transport to ingest packets.
    /// </summary>
    private void StartBackgroundListeners()
    {
        var packetTask = Task.Run(async () =>
        {
            try
            {
                var reader = _transport.PacketChannel;
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        if (!await reader.WaitToReadAsync(_shutdownCts.Token)) break;
                        while (reader.TryRead(out var p))
                        {
                            _packetHandler.IngestPacket(p.Buf, p.From, p.Timestamp);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Packet listener error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Packet listener crashed");
            }
        }, _shutdownCts.Token);
        _backgroundTasks.Add(packetTask);
    }
    
    /// <summary>
    /// Sends a raw UDP packet to a given Address via the transport.
    /// </summary>
    internal async Task SendPacketAsync(byte[] buffer, Transport.Address addr, CancellationToken cancellationToken = default)
    {
        await _transport.WriteToAddressAsync(buffer, addr, cancellationToken);
    }
    
    /// <summary>
    /// Initializes the local node with address from transport.
    /// </summary>
    private async Task InitializeLocalNodeAsync()
    {
        // Get advertise address from transport
        var (ip, port) = _transport.FinalAdvertiseAddr(_config.AdvertiseAddr, _config.AdvertisePort);
        
        lock (_advertiseLock)
        {
            _advertiseAddr = ip;
            _advertisePort = (ushort)port;
        }
        
        // Create local node
        _localNode = new Node
        {
            Name = _config.Name,
            Addr = ip,
            Port = (ushort)port,
            Meta = Array.Empty<byte>(), // TODO: Get from delegate
            State = NodeStateType.Alive,
            PMin = ProtocolVersion.Min,
            PMax = ProtocolVersion.Max,
            PCur = _config.ProtocolVersion,
            DMin = _config.DelegateProtocolMin,
            DMax = _config.DelegateProtocolMax,
            DCur = _config.DelegateProtocolVersion
        };
        
        // Add ourselves to the node map
        var localState = new NodeState
        {
            Node = _localNode,
            State = NodeStateType.Alive,
            StateChange = DateTimeOffset.UtcNow,
            Incarnation = 0
        };
        
        _nodeMap[_config.Name] = localState;
        
        lock (_nodeLock)
        {
            _nodes.Add(localState);
        }
        
        await Task.CompletedTask;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ShutdownAsync().GetAwaiter().GetResult();
            _shutdownCts.Dispose();
        }
    }
}
