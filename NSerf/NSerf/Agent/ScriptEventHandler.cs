// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// Event handler that invokes scripts based on event filters.
/// Supports hot-reload of scripts without restarting.
/// Maps to: Go's ScriptEventHandler in event_handler.go
/// </summary>
public class ScriptEventHandler(Func<Member> selfFunc, EventScript[]? scripts, ILogger? logger = null) : IEventHandler
{
    private readonly Func<Member> _selfFunc = selfFunc ?? throw new ArgumentNullException(nameof(selfFunc));
    private readonly object _scriptLock = new();
    private EventScript[] _scripts = scripts ?? [];
    private EventScript[]? _newScripts;  // Staged for atomic swap

    public void HandleEvent(IEvent @event)
    {
        // Atomic swap of scripts if update pending (hot-reload)
        lock (_scriptLock)
        {
            if (_newScripts != null)
            {
                _scripts = _newScripts;
                _newScripts = null;
                logger?.LogInformation("[Agent/Scripts] Hot-reloaded {Count} event scripts", _scripts.Length);
            }
        }

        var self = _selfFunc();

        foreach (var script in _scripts)
        {
            if (!script.Filter.Matches(@event))
                continue;

            // Execute a script asynchronously (don't block event loop)
            _ = Task.Run(async () =>
            {
                try
                {
                    await InvokeScriptAsync(script, self, @event);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "[Agent/Scripts] Error invoking script '{Script}': {Message}",
                        script.Script, ex.Message);
                }
            });
        }
    }

    /// <summary>
    /// Updates scripts for hot-reload. Changes take effect on the next event.
    /// Maps to: Go's UpdateScripts() in event_handler.go
    /// </summary>
    public void UpdateScripts(EventScript[] scripts)
    {
        lock (_scriptLock)
        {
            _newScripts = scripts;
        }
    }

    private async Task InvokeScriptAsync(EventScript script, Member self, IEvent @event)
    {
        var envVars = ScriptInvoker.BuildEnvironmentVariables(self, @event);
        var stdin = ScriptInvoker.BuildStdin(@event);

        var result = await ScriptInvoker.ExecuteAsync(
            script.Script,
            envVars,
            stdin,
            logger,
            timeout: TimeSpan.FromSeconds(30),
            evt: @event);

        if (result.ExitCode != 0)
        {
            logger?.LogWarning("[Agent/Scripts] Script '{Script}' exited with code {ExitCode}",
                script.Script, result.ExitCode);
        }
    }
}
