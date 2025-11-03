// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/lamport.go

using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// LamportTime is the value of a Lamport Clock.
/// Represents a logical timestamp in a distributed system.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
public readonly partial struct LamportTime : IEquatable<LamportTime>, IComparable<LamportTime>
{
    [Key(0)]
    private readonly ulong _value;

    public LamportTime(ulong value)
    {
        _value = value;
    }

    // Implicit conversion from ulong
    public static implicit operator LamportTime(ulong value) => new(value);

    // Implicit conversion to ulong
    public static implicit operator ulong(LamportTime time) => time._value;

    // Comparison operators
    public static bool operator <(LamportTime left, LamportTime right) => left._value < right._value;
    public static bool operator >(LamportTime left, LamportTime right) => left._value > right._value;
    public static bool operator <=(LamportTime left, LamportTime right) => left._value <= right._value;
    public static bool operator >=(LamportTime left, LamportTime right) => left._value >= right._value;
    public static bool operator ==(LamportTime left, LamportTime right) => left._value == right._value;
    public static bool operator !=(LamportTime left, LamportTime right) => left._value != right._value;

    // Arithmetic operators
    public static LamportTime operator +(LamportTime left, ulong right) => new(left._value + right);
    public static LamportTime operator -(LamportTime left, ulong right) => new(left._value - right);
    public static LamportTime operator ++(LamportTime time) => new(time._value + 1);

    public bool Equals(LamportTime other) => _value == other._value;
    public override bool Equals(object? obj) => obj is LamportTime other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public int CompareTo(LamportTime other) => _value.CompareTo(other._value);
    public override string ToString() => _value.ToString();
}

/// <summary>
/// LamportClock is a thread-safe implementation of a Lamport clock.
/// It uses efficient atomic operations for all of its functions, falling back
/// to retry logic using compare-and-swap if there are CAS failures.
/// 
/// Lamport clocks provide a logical ordering of events in a distributed system
/// where physical clock synchronization is not reliable.
/// </summary>
public class LamportClock
{
    private ulong _counter;

    /// <summary>
    /// Returns the current value of the Lamport clock.
    /// This operation is thread-safe and lock-free.
    /// </summary>
    /// <returns>The current logical timestamp</returns>
    public LamportTime Time()
    {
        return Interlocked.Read(ref _counter);
    }

    /// <summary>
    /// Increments and returns the value of the Lamport clock.
    /// This operation is thread-safe and atomic.
    /// </summary>
    /// <returns>The new logical timestamp after increment</returns>
    public LamportTime Increment()
    {
        return Interlocked.Increment(ref _counter);
    }

    /// <summary>
    /// Updates the local clock if necessary after witnessing a clock value
    /// received from another process. If the witnessed value is greater than
    /// or equal to the current value, the clock is updated to be one ahead
    /// of the witnessed value, maintaining the "happened before" relationship.
    /// 
    /// This operation is thread-safe using compare-and-swap with retry logic.
    /// </summary>
    /// <param name="v">The witnessed Lamport time from another process</param>
    public void Witness(LamportTime v)
    {
        // Retry loop for CAS failures
        while (true)
        {
            // If the other value is old, we do not need to do anything
            var current = Interlocked.Read(ref _counter);
            var other = (ulong)v;

            if (other < current)
            {
                return;
            }

            // Ensure that our local clock is at least one ahead
            // This maintains the "happened before" relationship
            var newValue = other + 1;

            if (Interlocked.CompareExchange(ref _counter, newValue, current) == current)
            {
                // CAS succeeded, we're done
                return;
            }

            // CAS failed, retry. Eventually either:
            // 1. Our CAS will succeed
            // 2. Another thread will advance the clock past 'other' and we'll return early
        }
    }
}
