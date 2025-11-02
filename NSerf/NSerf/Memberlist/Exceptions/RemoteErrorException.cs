// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Exceptions;

/// <summary>
/// Exception thrown when a remote node returns an error message during communication.
/// This allows distinguishing between local errors and errors reported by the remote node.
/// </summary>
public class RemoteErrorException : Exception
{
    /// <summary>
    /// The remote address that returned the error.
    /// </summary>
    public string? RemoteAddress { get; }
    
    /// <summary>
    /// The error message from the remote node.
    /// </summary>
    public string RemoteError { get; }
    
    /// <summary>
    /// Creates a new RemoteErrorException.
    /// </summary>
    /// <param name="remoteError">The error message from the remote node.</param>
    /// <param name="remoteAddress">Optional remote address that returned the error.</param>
    public RemoteErrorException(string remoteError, string? remoteAddress = null)
        : base($"Remote node{(remoteAddress != null ? $" ({remoteAddress})" : "")} returned error: {remoteError}")
    {
        RemoteError = remoteError;
        RemoteAddress = remoteAddress;
    }
    
    /// <summary>
    /// Creates a new RemoteErrorException with an inner exception.
    /// </summary>
    /// <param name="remoteError">The error message from the remote node.</param>
    /// <param name="remoteAddress">Optional remote address that returned the error.</param>
    /// <param name="innerException">The inner exception.</param>
    public RemoteErrorException(string remoteError, string? remoteAddress, Exception innerException)
        : base($"Remote node{(remoteAddress != null ? $" ({remoteAddress})" : "")} returned error: {remoteError}", innerException)
    {
        RemoteError = remoteError;
        RemoteAddress = remoteAddress;
    }
}
