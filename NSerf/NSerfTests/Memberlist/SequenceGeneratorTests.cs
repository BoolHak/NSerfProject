using NSerf.Memberlist;

namespace NSerfTests.Memberlist;

public class SequenceGeneratorTests
{
    [Fact]
    public void NextSeqNo_ShouldIncrement()
    {
        var gen = new SequenceGenerator();
        
        var first = gen.NextSeqNo();
        var second = gen.NextSeqNo();
        
        second.Should().Be(first + 1);
    }
    
    [Fact]
    public void NextIncarnation_ShouldIncrement()
    {
        var gen = new SequenceGenerator();
        
        var first = gen.NextIncarnation();
        var second = gen.NextIncarnation();
        
        second.Should().Be(first + 1);
    }
    
    [Fact]
    public void SkipIncarnation_ShouldAddOffset()
    {
        var gen = new SequenceGenerator();
        
        gen.NextIncarnation();
        var result = gen.SkipIncarnation(10);
        
        result.Should().Be(11);
    }
    
    [Fact]
    public void CurrentSeqNo_ShouldReturnCurrentValue()
    {
        var gen = new SequenceGenerator();
        
        gen.NextSeqNo();
        gen.NextSeqNo();
        
        gen.CurrentSeqNo.Should().Be(2);
    }
    
    [Fact]
    public void ThreadSafety_MultipleThreads_ShouldGenerateUniqueSequences()
    {
        var gen = new SequenceGenerator();
        var sequences = new System.Collections.Concurrent.ConcurrentBag<uint>();
        
        Parallel.For(0, 100, _ =>
        {
            sequences.Add(gen.NextSeqNo());
        });
        
        sequences.Distinct().Count().Should().Be(100);
    }
}
