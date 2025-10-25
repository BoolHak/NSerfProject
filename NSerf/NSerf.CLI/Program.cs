// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;

var rootCommand = new RootCommand("NSerf - Service orchestration and discovery tool");

// Add commands
rootCommand.Add(MembersCommand.Create());
rootCommand.Add(JoinCommand.Create());
rootCommand.Add(LeaveCommand.Create());
rootCommand.Add(ForceLeaveCommand.Create());
rootCommand.Add(EventCommand.Create());
rootCommand.Add(QueryCommand.Create());
rootCommand.Add(TagsCommand.Create());
rootCommand.Add(InfoCommand.Create());
rootCommand.Add(MonitorCommand.Create());
rootCommand.Add(KeygenCommand.Create());
rootCommand.Add(KeysCommand.Create());
rootCommand.Add(RttCommand.Create());
rootCommand.Add(ReachabilityCommand.Create());
rootCommand.Add(VersionCommand.Create());

return await rootCommand.Parse(args).InvokeAsync();
