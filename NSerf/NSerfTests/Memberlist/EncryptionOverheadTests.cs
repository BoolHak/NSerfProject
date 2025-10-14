using NSerf.Memberlist;

namespace NSerfTests.Memberlist;

public class EncryptionOverheadTests
{
    [Fact]
    public void GetOverhead_Version0_ShouldIncludePadding()
    {
        var overhead = EncryptionOverhead.GetOverhead(EncryptionVersion.Version0);
        
        overhead.Should().Be(45); // 1 + 12 + 16 + 16
    }
    
    [Fact]
    public void GetOverhead_Version1_ShouldNotIncludePadding()
    {
        var overhead = EncryptionOverhead.GetOverhead(EncryptionVersion.Version1);
        
        overhead.Should().Be(29); // 1 + 12 + 16
    }
    
    [Fact]
    public void EncryptedLength_Version0_ShouldAddFullOverhead()
    {
        var length = EncryptionOverhead.EncryptedLength(EncryptionVersion.Version0, 100);
        
        length.Should().Be(145); // 100 + 45
    }
    
    [Fact]
    public void EncryptedLength_Version1_ShouldAddReducedOverhead()
    {
        var length = EncryptionOverhead.EncryptedLength(EncryptionVersion.Version1, 100);
        
        length.Should().Be(129); // 100 + 29
    }
}
