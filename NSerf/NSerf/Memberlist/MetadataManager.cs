// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;

namespace NSerf.Memberlist;

/// <summary>
/// Manages node metadata.
/// </summary>
public class MetadataManager(IDelegate? delegateHandler = null, ILogger? logger = null)
{
    private readonly IDelegate? _delegate = delegateHandler;
    private readonly ILogger? _logger = logger;
    private byte[] _localMeta = [];
    private readonly object _lock = new();

    /// <summary>
    /// Gets the local node metadata.
    /// </summary>
    public byte[] GetLocalMeta()
    {
        lock (_lock)
        {
            return _localMeta;
        }
    }

    /// <summary>
    /// Updates the local node metadata from delegate.
    /// </summary>
    public byte[] RefreshLocalMeta()
    {
        try
        {
            var meta = _delegate?.LocalState(false) ?? [];

            lock (_lock)
            {
                _localMeta = meta;
            }

            return meta;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error refreshing local metadata");
            return [];
        }
    }

    /// <summary>
    /// Validates metadata size.
    /// </summary>
    public bool ValidateMetaSize(byte[] meta, int maxSize = 512)
    {
        if (meta.Length > maxSize)
        {
            _logger?.LogWarning("Metadata too large: {Size} > {Max}", meta.Length, maxSize);
            return false;
        }
        return true;
    }
}
