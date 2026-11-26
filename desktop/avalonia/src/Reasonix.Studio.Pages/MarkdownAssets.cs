using Avalonia.Media.Imaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

/// <summary>Resolves presentation assets through the owning workspace.</summary>
/// <param name="Resolve">Resolves an explicit Markdown destination to a workspace path.</param>
/// <param name="Open">Opens a resolved file in the workbench.</param>
/// <param name="ReadImage">Reads a resolved image under the existing file grant.</param>
public sealed record MarkdownAssets(Func<string, string?> Resolve, Func<string, Task> Open, Func<string, CancellationToken, Task<Bitmap?>> ReadImage);
