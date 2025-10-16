// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/query.go

using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// QueryParam is provided to Query() to configure the parameters of the query.
/// If not provided, sane defaults will be used.
/// </summary>
public class QueryParam
{
    /// <summary>
    /// If provided, we restrict the nodes that should respond to those
    /// with names in this list.
    /// </summary>
    public string[]? FilterNodes { get; set; }

    /// <summary>
    /// FilterTags maps a tag name to a regular expression that is applied
    /// to restrict the nodes that should respond.
    /// </summary>
    public Dictionary<string, string>? FilterTags { get; set; }

    /// <summary>
    /// If true, we are requesting a delivery acknowledgement from
    /// every node that meets the filter requirement. This means nodes
    /// that receive the message but do not pass the filters, will not
    /// send an ack.
    /// </summary>
    public bool RequestAck { get; set; }

    /// <summary>
    /// RelayFactor controls the number of duplicate responses to relay
    /// back to the sender through other nodes for redundancy.
    /// </summary>
    public byte RelayFactor { get; set; }

    /// <summary>
    /// The timeout limits how long the query is left open. If not provided,
    /// then a default timeout is used based on the configuration of Serf.
    /// </summary>
    public TimeSpan Timeout { get; set; }

    /// <summary>
    /// Encodes the filters into the wire format.
    /// Returns a list of encoded filters.
    /// </summary>
    public List<byte[]> EncodeFilters()
    {
        var filters = new List<byte[]>();

        // Add the node filter
        if (FilterNodes != null && FilterNodes.Length > 0)
        {
            var buf = EncodeFilter(FilterType.Node, FilterNodes);
            filters.Add(buf);
        }

        // Add the tag filters
        if (FilterTags != null)
        {
            foreach (var (tag, expr) in FilterTags)
            {
                var filt = new FilterTag { Tag = tag, Expr = expr };
                var buf = EncodeFilter(FilterType.Tag, filt);
                filters.Add(buf);
            }
        }

        return filters;
    }

    private static byte[] EncodeFilter<T>(FilterType filterType, T data)
    {
        var payload = MessagePackSerializer.Serialize(data);
        var result = new byte[payload.Length + 1];
        result[0] = (byte)filterType;
        Array.Copy(payload, 0, result, 1, payload.Length);
        return result;
    }
}
