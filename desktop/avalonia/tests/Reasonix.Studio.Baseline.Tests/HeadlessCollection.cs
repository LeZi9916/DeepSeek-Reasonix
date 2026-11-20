using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Marks the cases that start an Avalonia headless session, so they run one at a
/// time.
/// </summary>
/// <remarks>
/// A headless session registers the <c>avares:</c> URI parser with the process
/// the first time it starts. Two sessions on two xUnit workers racing to register
/// it fail with "a URI scheme name already has a registered custom parser", and a
/// session started on one worker cannot be touched from another. Both are about
/// the process, not about the code under test, so the cases sharing it are one
/// collection and the collection opts out of parallel execution.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessCollection
{
    /// <summary>
    /// The name the cases cite to join this collection.
    /// </summary>
    public const string Name = "avalonia-headless";
}
