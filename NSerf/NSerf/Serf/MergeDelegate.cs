// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/merge_delegate.go

using NSerf.Memberlist.State;
using System.Net;

namespace NSerf.Serf;

/// <summary>
/// MergeDelegate is the Serf internal implementation of Memberlist's IMergeDelegate.
/// It converts memberlist Nodes to Serf Members and forwards to the user's merge delegate.
/// </summary>
/// <remarks>
/// Creates a new MergeDelegate for the given Serf instance.
/// </remarks>
/// <param name="serf">The Serf instance</param>
/// <exception cref="ArgumentNullException">Thrown if serf is null</exception>
internal class MergeDelegate(Serf serf) : Memberlist.Delegates.IMergeDelegate
{
    private readonly Serf _serf = serf ?? throw new ArgumentNullException(nameof(serf));

    /// <summary>
    /// Called by memberlist when a merge could take place.
    /// Converts nodes to members and forwards to user's merge delegate.
    /// </summary>
    /// <param name="peers">List of nodes from the remote peer</param>
    /// <returns>Error message if merge should be canceled, null to allow</returns>
    public string? NotifyMerge(IReadOnlyList<Node> peers)
    {
        // Convert all nodes to members
        var members = new List<Member>(peers.Count);

        foreach (var node in peers)
        {
            var (member, error) = NodeToMember(node);
            if (error != null)
            {
                return error;
            }
            members.Add(member!);
        }

        // Forward to user's merge delegate if configured
        if (_serf.Config.Merge != null)
        {
            // Note: IMergeDelegate.NotifyMerge is async, but memberlist's IMergeDelegate is sync
            // We need to block here (will be refactored in Phase 9)
            return _serf.Config.Merge.NotifyMerge(members.ToArray()).GetAwaiter().GetResult();
        }

        return null;  // Allow merge by default
    }

    /// <summary>
    /// Converts a memberlist Node to a Serf Member.
    /// </summary>
    /// <param name="node">The memberlist node</param>
    /// <returns>Tuple of (Member, error message)</returns>
    public (Member?, string?) NodeToMember(Node node)
    {
        // Validate the node info first
        var validationError = ValidateMemberInfo(node);
        if (validationError != null)
        {
            return (null, validationError);
        }

        // Determine status based on memberlist state
        var status = MemberStatus.None;
        if (node.State == NodeStateType.Left)
        {
            status = MemberStatus.Left;
        }

        // Convert node to member
        var member = new Member
        {
            Name = node.Name,
            Addr = node.Addr,
            Port = node.Port,
            Tags = Serf.DecodeTags(node.Meta),
            Status = status,
            ProtocolMin = node.PMin,
            ProtocolMax = node.PMax,
            ProtocolCur = node.PCur,
            DelegateMin = node.DMin,
            DelegateMax = node.DMax,
            DelegateCur = node.DCur
        };

        return (member, null);
    }

    /// <summary>
    /// Validates that the node information is valid.
    /// </summary>
    /// <param name="node">The node to validate</param>
    /// <returns>Error message if invalid, null if valid</returns>
    public string? ValidateMemberInfo(Node node)
    {
        // Validate node name if enabled
        var nameError = _serf.ValidateNodeName(node.Name);
        if (nameError != null)
        {
            return nameError;
        }

        // Validate IP address length
        var ipBytes = node.Addr.GetAddressBytes();
        var ipError = ValidateIPLength(ipBytes);
        if (ipError != null)
        {
            return ipError;
        }

        // Validate metadata size
        if (node.Meta.Length > 512)  // memberlist.MetaMaxSize = 512
        {
            return $"Encoded length of tags exceeds limit of 512 bytes";
        }

        return null;
    }

    /// <summary>
    /// Validates IP address byte length (must be 4 for IPv4 or 16 for IPv6).
    /// </summary>
    /// <param name="ipBytes">The IP address bytes</param>
    /// <returns>Error message if invalid, null if valid</returns>
    public static string? ValidateIPLength(byte[] ipBytes)
    {
        if (ipBytes.Length != 4 && ipBytes.Length != 16)
        {
            return $"IP byte length is invalid: {ipBytes.Length} bytes is not either 4 or 16";
        }
        return null;
    }
}
