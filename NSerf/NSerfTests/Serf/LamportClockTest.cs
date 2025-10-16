// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/lamport_test.go

using NSerf.Serf;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Lamport Clock implementation.
/// Verifies thread-safe logical clock operations.
/// </summary>
public class LamportClockTest
{
    [Fact]
    public void LamportClock_InitialValue_ShouldBeZero()
    {
        // Arrange & Act
        var clock = new LamportClock();

        // Assert
        clock.Time().Should().Be(0, "initial time should be 0");
    }

    [Fact]
    public void Increment_FirstCall_ShouldReturnOne()
    {
        // Arrange
        var clock = new LamportClock();

        // Act
        var time = clock.Increment();

        // Assert
        time.Should().Be(1, "first increment should return 1");
    }

    [Fact]
    public void Time_AfterIncrement_ShouldReturnIncrementedValue()
    {
        // Arrange
        var clock = new LamportClock();
        clock.Increment();

        // Act
        var time = clock.Time();

        // Assert
        time.Should().Be(1, "time should reflect the increment");
    }

    [Fact]
    public void Witness_WithHigherValue_ShouldUpdateClockToOneAhead()
    {
        // Arrange
        var clock = new LamportClock();
        clock.Increment(); // time = 1

        // Act
        clock.Witness(41);

        // Assert
        clock.Time().Should().Be(42, "witnessing 41 should update clock to 42");
    }

    [Fact]
    public void Witness_WithSameValue_ShouldNotChangeTime()
    {
        // Arrange
        var clock = new LamportClock();
        clock.Witness(41); // Sets to 42
        var timeBefore = clock.Time();

        // Act
        clock.Witness(41);

        // Assert
        clock.Time().Should().Be(timeBefore, "witnessing same value should not change time");
        clock.Time().Should().Be(42);
    }

    [Fact]
    public void Witness_WithLowerValue_ShouldNotChangeTime()
    {
        // Arrange
        var clock = new LamportClock();
        clock.Witness(41); // Sets to 42

        // Act
        clock.Witness(30);

        // Assert
        clock.Time().Should().Be(42, "witnessing lower value should not change time");
    }

    [Fact]
    public void Increment_MultipleTimes_ShouldIncrementSequentially()
    {
        // Arrange
        var clock = new LamportClock();

        // Act & Assert
        clock.Increment().Should().Be(1);
        clock.Increment().Should().Be(2);
        clock.Increment().Should().Be(3);
        clock.Time().Should().Be(3);
    }

    [Fact]
    public void Witness_Sequence_ShouldHandleMultipleUpdates()
    {
        // Arrange
        var clock = new LamportClock();

        // Act & Assert - witness sequence from Go test
        clock.Time().Should().Be(0);
        
        clock.Increment().Should().Be(1);
        clock.Time().Should().Be(1);
        
        clock.Witness(41);
        clock.Time().Should().Be(42);
        
        clock.Witness(41);
        clock.Time().Should().Be(42);
        
        clock.Witness(30);
        clock.Time().Should().Be(42);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentIncrements_ShouldBeAtomic()
    {
        // Arrange
        var clock = new LamportClock();
        const int threadCount = 10;
        const int incrementsPerThread = 1000;
        var tasks = new List<Task>();

        // Act - multiple threads incrementing concurrently
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < incrementsPerThread; j++)
                {
                    clock.Increment();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - should have exact count
        var expectedValue = threadCount * incrementsPerThread;
        clock.Time().Should().Be((ulong)expectedValue, 
            "all increments should be atomic and counted");
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentWitness_ShouldBeConsistent()
    {
        // Arrange
        var clock = new LamportClock();
        const int threadCount = 10;
        var tasks = new List<Task>();

        // Act - multiple threads witnessing different values
        for (int i = 0; i < threadCount; i++)
        {
            var witnessValue = (ulong)(i * 10);
            tasks.Add(Task.Run(() =>
            {
                clock.Witness(witnessValue);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - clock should be at least as high as the highest witnessed value + 1
        clock.Time().Should().BeGreaterOrEqualTo(91, 
            "clock should be at least highest witness (90) + 1");
    }

    [Fact]
    public async Task ThreadSafety_MixedOperations_ShouldBeConsistent()
    {
        // Arrange
        var clock = new LamportClock();
        const int operationsPerThread = 100;
        var tasks = new List<Task>();

        // Act - mix of increments, witness, and reads
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    switch (j % 3)
                    {
                        case 0:
                            clock.Increment();
                            break;
                        case 1:
                            clock.Witness((ulong)(j * 2));
                            break;
                        case 2:
                            _ = clock.Time();
                            break;
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - clock should be monotonically increasing (no corruption)
        var finalTime = clock.Time();
        finalTime.Should().BeGreaterThan(0, "clock should have advanced");
    }

    [Fact]
    public void LamportTime_ImplicitConversion_ShouldWork()
    {
        // Arrange
        var clock = new LamportClock();
        clock.Increment();

        // Act
        LamportTime time = clock.Time();
        ulong value = time;

        // Assert
        value.Should().Be(1);
    }

    [Fact]
    public void LamportTime_Comparison_ShouldWork()
    {
        // Arrange
        LamportTime time1 = 10;
        LamportTime time2 = 20;
        LamportTime time3 = 10;

        // Act & Assert
        (time1 < time2).Should().BeTrue();
        (time2 > time1).Should().BeTrue();
        (time1 == time3).Should().BeTrue();
        (time1 != time2).Should().BeTrue();
    }
}
