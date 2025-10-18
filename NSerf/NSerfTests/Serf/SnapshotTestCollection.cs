using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Collection definition to ensure snapshot tests run sequentially, not in parallel.
/// This prevents port conflicts and test pollution.
/// </summary>
[CollectionDefinition("Sequential Snapshot Tests", DisableParallelization = true)]
public class SnapshotTestCollection
{
    // This class is never instantiated. It's only used to define the collection.
}
