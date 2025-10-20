// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Helpers;

/// <summary>
/// Provides generic lock management utilities with automatic cleanup.
/// Implements C# try-finally patterns equivalent to Go's defer unlock().
/// These helpers ensure locks are always released even if exceptions occur.
/// </summary>
public static class LockHelper
{
    /// <summary>
    /// Executes an action while holding a read lock. Ensures lock is released.
    /// Pattern: C# try-finally equivalent of Go's defer lock.RUnlock()
    /// </summary>
    public static void WithReadLock(ReaderWriterLockSlim lockObj, Action action)
    {
        lockObj.EnterReadLock();
        try
        {
            action();
        }
        finally
        {
            lockObj.ExitReadLock();
        }
    }

    /// <summary>
    /// Executes a function while holding a read lock. Ensures lock is released.
    /// Returns the result of the function.
    /// </summary>
    public static T WithReadLock<T>(ReaderWriterLockSlim lockObj, Func<T> func)
    {
        lockObj.EnterReadLock();
        try
        {
            return func();
        }
        finally
        {
            lockObj.ExitReadLock();
        }
    }

    /// <summary>
    /// Executes an action while holding a write lock. Ensures lock is released.
    /// Pattern: C# try-finally equivalent of Go's defer lock.Unlock()
    /// </summary>
    public static void WithWriteLock(ReaderWriterLockSlim lockObj, Action action)
    {
        lockObj.EnterWriteLock();
        try
        {
            action();
        }
        finally
        {
            lockObj.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes a function while holding a write lock. Ensures lock is released.
    /// Returns the result of the function.
    /// </summary>
    public static T WithWriteLock<T>(ReaderWriterLockSlim lockObj, Func<T> func)
    {
        lockObj.EnterWriteLock();
        try
        {
            return func();
        }
        finally
        {
            lockObj.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes an action while holding a semaphore lock. Ensures lock is released.
    /// </summary>
    public static void WithLock(SemaphoreSlim semaphore, Action action)
    {
        semaphore.Wait();
        try
        {
            action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes a function while holding a semaphore lock. Ensures lock is released.
    /// Returns the result of the function.
    /// </summary>
    public static T WithLock<T>(SemaphoreSlim semaphore, Func<T> func)
    {
        semaphore.Wait();
        try
        {
            return func();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes an async action while holding a semaphore lock. Ensures lock is released.
    /// </summary>
    public static async Task WithLockAsync(SemaphoreSlim semaphore, Func<Task> action)
    {
        await semaphore.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes an async function while holding a semaphore lock. Ensures lock is released.
    /// Returns the result of the async function.
    /// </summary>
    public static async Task<T> WithLockAsync<T>(SemaphoreSlim semaphore, Func<Task<T>> func)
    {
        await semaphore.WaitAsync();
        try
        {
            return await func();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
