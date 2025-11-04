// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class CoordinateResponse
{
    [Key(0)]
    public bool Ok { get; set; }
    
    [Key(1)]
    public Coordinate? Coord { get; set; }
}

[MessagePackObject]
public class Coordinate
{
    [Key(0)]
    public float[] Vec { get; set; } = [];
    
    [Key(1)]
    public float Error { get; set; }
    
    [Key(2)]
    public float Adjustment { get; set; }
    
    [Key(3)]
    public float Height { get; set; }
}
