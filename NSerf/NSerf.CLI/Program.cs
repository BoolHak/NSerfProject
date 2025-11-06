// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;

var rootCommand = new RootCommand("NSerf - Service orchestration and discovery tool")
{
    // Add commands
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
    VersionCommand.Create()
};

return await rootCommand.Parse(args).InvokeAsync();
