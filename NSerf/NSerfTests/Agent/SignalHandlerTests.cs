// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class SignalHandlerTests
{
    [Fact]
    public void SignalHandler_RegisterCallback_InvokesOnSignal()
    {
        var handler = new SignalHandler();
        Signal? receivedSignal = null;
        
        handler.RegisterCallback(sig => receivedSignal = sig);
        handler.TriggerSignal(Signal.SIGINT);
        
        Assert.Equal(Signal.SIGINT, receivedSignal);
    }

    [Fact]
    public void SignalHandler_MultipleCallbacks_AllInvoked()
    {
        var handler = new SignalHandler();
        int callCount = 0;
        
        handler.RegisterCallback(_ => callCount++);
        handler.RegisterCallback(_ => callCount++);
        handler.RegisterCallback(_ => callCount++);
        
        handler.TriggerSignal(Signal.SIGTERM);
        
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void SignalHandler_CallbackException_DoesNotStopOthers()
    {
        var handler = new SignalHandler();
        int successCount = 0;
        
        handler.RegisterCallback(_ => throw new Exception("Callback error"));
        handler.RegisterCallback(_ => successCount++);
        handler.RegisterCallback(_ => successCount++);
        
        handler.TriggerSignal(Signal.SIGHUP);
        
        Assert.Equal(2, successCount);
    }

    [Fact]
    public void SignalHandler_Dispose_CleansUpHandlers()
    {
        var handler = new SignalHandler();
        handler.Dispose();
        
        // Should not throw after dispose
        handler.TriggerSignal(Signal.SIGINT);
    }
}
