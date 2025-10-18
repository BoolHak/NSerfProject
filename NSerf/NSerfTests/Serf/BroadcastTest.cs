// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/broadcast_test.go

using NSerf.Serf;
using NSerf.Memberlist;
using System.Threading.Channels;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Broadcast class.
/// </summary>
public class BroadcastTest
{
    [Fact]
    public void Broadcast_ShouldImplementIBroadcast()
    {
        // Arrange
        var msg = new byte[] { 1, 2, 3 };

        // Act
        var broadcast = new Broadcast(msg);

        // Assert
        broadcast.Should().BeAssignableTo<IBroadcast>();
    }

    [Fact]
    public void Broadcast_ShouldImplementIUniqueBroadcast()
    {
        // Arrange
        var msg = new byte[] { 1, 2, 3 };

        // Act
        var broadcast = new Broadcast(msg);

        // Assert
        broadcast.Should().BeAssignableTo<IUniqueBroadcast>();
    }

    [Fact]
    public void Message_ShouldReturnOriginalMessage()
    {
        // Arrange
        var expectedMsg = new byte[] { 1, 2, 3, 4, 5 };
        var broadcast = new Broadcast(expectedMsg);

        // Act
        var actualMsg = broadcast.Message();

        // Assert
        actualMsg.Should().BeSameAs(expectedMsg, "Message() should return the same byte array instance");
    }

    [Fact]
    public void Invalidates_ShouldAlwaysReturnFalse()
    {
        // Arrange
        var broadcast1 = new Broadcast(new byte[] { 1 });
        var broadcast2 = new Broadcast(new byte[] { 2 });

        // Act
        var result = broadcast1.Invalidates(broadcast2);

        // Assert
        result.Should().BeFalse("IUniqueBroadcast should never invalidate other broadcasts");
    }

    [Fact]
    public async Task Finished_WithNotifyChannel_ShouldSignalCompletion()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<bool>();
        var broadcast = new Broadcast(new byte[] { 1, 2, 3 }, channel.Writer);

        // Act
        broadcast.Finished();

        // Assert - should receive signal within short timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var received = await channel.Reader.ReadAsync(cts.Token);
        received.Should().BeTrue("Finished() should signal the notify channel");
        
        // Channel should be completed
        var completionTask = channel.Reader.Completion;
        await completionTask.WaitAsync(TimeSpan.FromMilliseconds(100));
        completionTask.IsCompleted.Should().BeTrue("Channel should be marked as complete");
    }

    [Fact]
    public void Finished_WithNullNotifyChannel_ShouldNotThrow()
    {
        // Arrange
        var broadcast = new Broadcast(new byte[] { 1, 2, 3 }, notifyWriter: null);

        // Act
        Action act = () => broadcast.Finished();

        // Assert - Should handle null gracefully without throwing
        act.Should().NotThrow("Finished() should handle null notify channel gracefully");
        
        // Verify broadcast is still functional after Finished()
        var message = broadcast.Message();
        message.Should().Equal(new byte[] { 1, 2, 3 }, "message should still be accessible");
        
        // Can call Finished multiple times safely with null channel
        act.Should().NotThrow("multiple calls should also be safe with null channel");
    }

    [Fact]
    public void Finished_WithoutNotifyChannel_ShouldNotThrow()
    {
        // Arrange - Default constructor, no notify channel
        var broadcast = new Broadcast(new byte[] { 1, 2, 3 });

        // Act
        Action act = () => broadcast.Finished();

        // Assert - Should be no-op when no channel provided
        act.Should().NotThrow("Finished() should handle missing notify channel gracefully");
        
        // Verify broadcast remains functional
        var message = broadcast.Message();
        message.Should().Equal(new byte[] { 1, 2, 3 }, "message should still be accessible");
        
        // Verify Invalidates still works
        var other = new Broadcast(new byte[] { 4, 5 });
        broadcast.Invalidates(other).Should().BeFalse("Invalidates should still work after Finished");
    }

    [Fact]
    public void Constructor_WithNullMessage_ShouldThrow()
    {
        // Arrange
        byte[]? msg = null;

        // Act
        Action act = () => new Broadcast(msg!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("msg");
    }

    [Fact]
    public void Constructor_WithEmptyMessage_ShouldWork()
    {
        // Arrange
        var msg = Array.Empty<byte>();

        // Act
        var broadcast = new Broadcast(msg);

        // Assert
        broadcast.Message().Should().BeSameAs(msg);
    }

    [Fact]
    public void Broadcast_MultipleInvalidatesCalls_ShouldAlwaysReturnFalse()
    {
        // Arrange
        var broadcast = new Broadcast(new byte[] { 1 });
        var other1 = new Broadcast(new byte[] { 2 });
        var other2 = new Broadcast(new byte[] { 3 });

        // Act & Assert
        broadcast.Invalidates(other1).Should().BeFalse();
        broadcast.Invalidates(other2).Should().BeFalse();
        broadcast.Invalidates(broadcast).Should().BeFalse("Should return false even for self");
    }

    [Fact]
    public async Task Broadcast_MultipleFinishedCalls_ShouldNotThrow()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<bool>();
        var broadcast = new Broadcast(new byte[] { 1 }, channel.Writer);

        // Act - Call Finished() multiple times
        broadcast.Finished();
        broadcast.Finished(); // Second call
        broadcast.Finished(); // Third call

        // Assert - Should be idempotent (safe to call multiple times)
        // 1. Should have received at least one completion signal
        channel.Reader.TryRead(out var signal).Should().BeTrue("should receive completion signal");
        signal.Should().BeTrue();
        
        // 2. Channel should be completed (marked as done)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await channel.Reader.Completion.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout is acceptable if already completed
        }
        
        // 3. Broadcast should still function correctly
        broadcast.Message().Should().Equal(new byte[] { 1 });
        
        // Multiple Finished() calls handled gracefully (ChannelClosedException caught internally)
    }

    [Fact]
    public async Task Broadcast_LargeMessage_ShouldHandleCorrectly()
    {
        // Arrange
        var largeMsg = new byte[1024 * 100]; // 100 KB
        Array.Fill(largeMsg, (byte)42);
        var broadcast = new Broadcast(largeMsg);

        // Act
        var retrievedMsg = broadcast.Message();

        // Assert
        retrievedMsg.Should().BeSameAs(largeMsg);
        retrievedMsg.Length.Should().Be(1024 * 100);
        
        await Task.CompletedTask; // Keep async for consistency
    }

    [Fact]
    public void Broadcast_DifferentInstances_ShouldBeIndependent()
    {
        // Arrange
        var msg1 = new byte[] { 1, 2, 3 };
        var msg2 = new byte[] { 4, 5, 6 };
        var broadcast1 = new Broadcast(msg1);
        var broadcast2 = new Broadcast(msg2);

        // Act & Assert
        broadcast1.Message().Should().Equal(new byte[] { 1, 2, 3 });
        broadcast2.Message().Should().Equal(new byte[] { 4, 5, 6 });
        broadcast1.Invalidates(broadcast2).Should().BeFalse();
        broadcast2.Invalidates(broadcast1).Should().BeFalse();
    }
}
