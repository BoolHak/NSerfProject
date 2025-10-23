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
public class ScriptEventHandler : IEventHandler
{
    private readonly Func<Member> _selfFunc;
    private readonly ILogger? _logger;
    private readonly object _scriptLock = new();
    private EventScript[] _scripts;
    private EventScript[]? _newScripts;  // Staged for atomic swap

    public ScriptEventHandler(Func<Member> selfFunc, EventScript[] scripts, ILogger? logger = null)
    {
        _selfFunc = selfFunc ?? throw new ArgumentNullException(nameof(selfFunc));
        _scripts = scripts ?? Array.Empty<EventScript>();
        _logger = logger;
    }

    public void HandleEvent(Event evt)
    {
        // Atomic swap of scripts if update pending (hot-reload)
        lock (_scriptLock)
        {
            if (_newScripts != null)
            {
                _scripts = _newScripts;
                _newScripts = null;
                _logger?.LogInformation("[Agent/Scripts] Hot-reloaded {Count} event scripts", _scripts.Length);
            }
        }

        var self = _selfFunc();

        foreach (var script in _scripts)
        {
            if (!script.Filter.Matches(evt))
                continue;

            // Execute script asynchronously (don't block event loop)
            _ = Task.Run(async () =>
            {
                try
                {
                    await InvokeScriptAsync(script, self, evt);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[Agent/Scripts] Error invoking script '{Script}': {Message}",
                        script.Script, ex.Message);
                }
            });
        }
    }

    /// <summary>
    /// Updates scripts for hot-reload. Changes take effect on next event.
    /// Maps to: Go's UpdateScripts() in event_handler.go
    /// </summary>
    public void UpdateScripts(EventScript[] scripts)
    {
        lock (_scriptLock)
        {
            _newScripts = scripts;
        }
    }

    private async Task InvokeScriptAsync(EventScript script, Member self, Event evt)
    {
        var envVars = ScriptInvoker.BuildEnvironmentVariables(self, evt);
        var stdin = ScriptInvoker.BuildStdin(evt);

        var result = await ScriptInvoker.ExecuteAsync(
            script.Script,
            envVars,
            stdin,
            _logger,
            timeout: TimeSpan.FromSeconds(30),
            evt: evt);

        if (result.ExitCode != 0)
        {
            _logger?.LogWarning("[Agent/Scripts] Script '{Script}' exited with code {ExitCode}",
                script.Script, result.ExitCode);
        }
    }
}
