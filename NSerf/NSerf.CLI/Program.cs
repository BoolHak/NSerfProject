// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var rootCommand = new RootCommand("NSerf - Service orchestration and discovery tool")
{
    // Add commands
    AgentCommand.Create(cts.Token), // Agent command must be first
    MembersCommand.Create(),
    JoinCommand.Create(),
    LeaveCommand.Create(),
    ForceLeaveCommand.Create(),
    EventCommand.Create(),
    QueryCommand.Create(),
    TagsCommand.Create(),
    InfoCommand.Create(),
    MonitorCommand.Create(),
    KeygenCommand.Create(),
    KeysCommand.Create(),
    RttCommand.Create(),
    ReachabilityCommand.Create(),
    VersionCommand.Create(),
    ConfigSecretsCommand.Create()
};

return await rootCommand.Parse(args).InvokeAsync();