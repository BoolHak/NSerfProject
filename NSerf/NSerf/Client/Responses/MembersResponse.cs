// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class MembersResponse
{
    [Key(0)]
    public Member[] Members { get; set; } = Array.Empty<Member>();
}
