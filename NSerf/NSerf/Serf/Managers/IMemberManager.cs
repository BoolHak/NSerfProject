// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Managers;

/// <summary>
/// Manages member state with transaction pattern for atomic operations.
/// Provides thread-safe access to member collections through ExecuteUnderLock.
/// </summary>
internal interface IMemberManager
{
    /// <summary>
    /// Executes an operation under lock with access to member state.
    /// Provides atomic access to member collections.
    /// </summary>
    /// <typeparam name="TResult">Return type of the operation</typeparam>
    /// <param name="operation">Operation to execute with member state access</param>
    /// <returns>Result of the operation</returns>
    TResult ExecuteUnderLock<TResult>(Func<IMemberStateAccessor, TResult> operation);
    
    /// <summary>
    /// Executes an operation under lock with access to member state (no return value).
    /// </summary>
    /// <param name="operation">Operation to execute with member state access</param>
    void ExecuteUnderLock(Action<IMemberStateAccessor> operation);
}
