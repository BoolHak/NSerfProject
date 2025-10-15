using FluentAssertions;
using NSerf.Memberlist.Exceptions;
using NSerf.Memberlist.Messages;
using Xunit;

namespace NSerfTests.Memberlist;

/// <summary>
/// Tests for error message encoding, decoding, and RemoteErrorException handling.
/// </summary>
public class ErrorMessageTests
{
    [Fact]
    public void RemoteErrorException_WithErrorOnly_ShouldFormatCorrectly()
    {
        // Arrange
        var errorMsg = "Connection timeout";
        
        // Act
        var exception = new RemoteErrorException(errorMsg);
        
        // Assert
        exception.RemoteError.Should().Be(errorMsg);
        exception.RemoteAddress.Should().BeNull();
        exception.Message.Should().Contain(errorMsg);
        exception.Message.Should().Contain("Remote node");
    }
    
    [Fact]
    public void RemoteErrorException_WithAddress_ShouldIncludeAddress()
    {
        // Arrange
        var errorMsg = "Invalid protocol version";
        var address = "192.168.1.100:7946";
        
        // Act
        var exception = new RemoteErrorException(errorMsg, address);
        
        // Assert
        exception.RemoteError.Should().Be(errorMsg);
        exception.RemoteAddress.Should().Be(address);
        exception.Message.Should().Contain(errorMsg);
        exception.Message.Should().Contain(address);
    }
    
    [Fact]
    public void RemoteErrorException_WithInnerException_ShouldPreserveInner()
    {
        // Arrange
        var errorMsg = "Deserialization failed";
        var address = "10.0.0.5:8080";
        var innerException = new InvalidOperationException("Bad data");
        
        // Act
        var exception = new RemoteErrorException(errorMsg, address, innerException);
        
        // Assert
        exception.RemoteError.Should().Be(errorMsg);
        exception.RemoteAddress.Should().Be(address);
        exception.InnerException.Should().BeSameAs(innerException);
        exception.Message.Should().Contain(errorMsg);
    }
    
    [Fact]
    public void ErrRespMessage_Serialization_ShouldRoundTrip()
    {
        // Arrange
        var originalError = new ErrRespMessage
        {
            Error = "Test error message"
        };
        
        // Act - Serialize
        var serialized = MessagePack.MessagePackSerializer.Serialize(originalError);
        
        // Act - Deserialize
        var deserialized = MessagePack.MessagePackSerializer.Deserialize<ErrRespMessage>(serialized);
        
        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Error.Should().Be(originalError.Error);
    }
    
    [Fact]
    public void ErrRespMessage_EmptyError_ShouldSerialize()
    {
        // Arrange
        var emptyError = new ErrRespMessage
        {
            Error = string.Empty
        };
        
        // Act
        var serialized = MessagePack.MessagePackSerializer.Serialize(emptyError);
        var deserialized = MessagePack.MessagePackSerializer.Deserialize<ErrRespMessage>(serialized);
        
        // Assert
        deserialized.Error.Should().BeEmpty();
    }
    
    [Fact]
    public void ErrRespMessage_LongError_ShouldSerialize()
    {
        // Arrange
        var longError = new string('X', 10000);
        var errResp = new ErrRespMessage
        {
            Error = longError
        };
        
        // Act
        var serialized = MessagePack.MessagePackSerializer.Serialize(errResp);
        var deserialized = MessagePack.MessagePackSerializer.Deserialize<ErrRespMessage>(serialized);
        
        // Assert
        deserialized.Error.Should().Be(longError);
        deserialized.Error.Length.Should().Be(10000);
    }
    
    [Fact]
    public void ErrRespMessage_SpecialCharacters_ShouldPreserve()
    {
        // Arrange
        var specialError = "Error: Connection refused\nStack trace:\n\tat Function()\n\u0000\u0001\u0002";
        var errResp = new ErrRespMessage
        {
            Error = specialError
        };
        
        // Act
        var serialized = MessagePack.MessagePackSerializer.Serialize(errResp);
        var deserialized = MessagePack.MessagePackSerializer.Deserialize<ErrRespMessage>(serialized);
        
        // Assert
        deserialized.Error.Should().Be(specialError);
    }
    
    [Fact]
    public void ErrRespMessage_UnicodeCharacters_ShouldPreserve()
    {
        // Arrange
        var unicodeError = "错误: 连接超时 🚫 тест エラー";
        var errResp = new ErrRespMessage
        {
            Error = unicodeError
        };
        
        // Act
        var serialized = MessagePack.MessagePackSerializer.Serialize(errResp);
        var deserialized = MessagePack.MessagePackSerializer.Deserialize<ErrRespMessage>(serialized);
        
        // Assert
        deserialized.Error.Should().Be(unicodeError);
    }
    
    [Fact]
    public void RemoteErrorException_CanBeCaught_AsRemoteError()
    {
        // Arrange
        Action throwError = () => throw new RemoteErrorException("Test error", "127.0.0.1:8000");
        
        // Act & Assert
        throwError.Should().Throw<RemoteErrorException>()
            .Which.RemoteError.Should().Be("Test error");
    }
    
    [Fact]
    public void RemoteErrorException_CanBeCaught_AsBaseException()
    {
        // Arrange
        Action throwError = () => throw new RemoteErrorException("Test error");
        
        // Act & Assert
        throwError.Should().Throw<Exception>()
            .Which.Should().BeOfType<RemoteErrorException>();
    }
    
    [Fact]
    public void RemoteErrorException_MessageFormat_NoAddress()
    {
        // Arrange & Act
        var exception = new RemoteErrorException("Connection failed");
        
        // Assert
        exception.Message.Should().Be("Remote node returned error: Connection failed");
    }
    
    [Fact]
    public void RemoteErrorException_MessageFormat_WithAddress()
    {
        // Arrange & Act
        var exception = new RemoteErrorException("Connection failed", "192.168.1.50:7946");
        
        // Assert
        exception.Message.Should().Be("Remote node (192.168.1.50:7946) returned error: Connection failed");
    }
    
    [Fact]
    public void ErrRespMessage_DefaultConstructor_ShouldHaveEmptyError()
    {
        // Arrange & Act
        var errResp = new ErrRespMessage();
        
        // Assert
        errResp.Error.Should().NotBeNull();
        errResp.Error.Should().BeEmpty();
    }
    
    [Theory]
    [InlineData("Timeout")]
    [InlineData("Connection refused")]
    [InlineData("Protocol version mismatch")]
    [InlineData("Authentication failed")]
    [InlineData("Rate limit exceeded")]
    public void ErrRespMessage_CommonErrors_ShouldRoundTrip(string errorMessage)
    {
        // Arrange
        var errResp = new ErrRespMessage { Error = errorMessage };
        
        // Act
        var serialized = MessagePack.MessagePackSerializer.Serialize(errResp);
        var deserialized = MessagePack.MessagePackSerializer.Deserialize<ErrRespMessage>(serialized);
        
        // Assert
        deserialized.Error.Should().Be(errorMessage);
    }
}
