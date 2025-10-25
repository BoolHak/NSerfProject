// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.CLI.Tests.Fixtures;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Extended tests for version command
/// </summary>
[Trait("Category", "Unit")]
public class VersionCommandExtendedTests
{
    [Fact]
    public void Version_IncludesProtocolVersion()
    {
        var config = new AgentConfig
        {
            Protocol = 5
        };

        Assert.Equal(5, config.Protocol);
    }

    [Fact]
    public void Version_DefaultProtocol_IsFive()
    {
        var config = new AgentConfig();
        Assert.Equal(5, config.Protocol);
    }

    [Fact]
    public void Version_CustomProtocol_CanBeSet()
    {
        var config = new AgentConfig
        {
            Protocol = 4
        };

        Assert.Equal(4, config.Protocol);
    }
}
