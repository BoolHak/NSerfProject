// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Represents a metric label for telemetry.
/// </summary>
public class MetricLabel
{
    /// <summary>
    /// Name of the label.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Value of the label.
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    public MetricLabel()
    {
    }
    
    public MetricLabel(string name, string value)
    {
        Name = name;
        Value = value;
    }
    
    public override string ToString()
    {
        return $"{Name}={Value}";
    }
}
