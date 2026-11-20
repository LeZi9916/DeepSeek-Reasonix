using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The conversation itself: every row the session has produced, in order.
/// </summary>
/// <remarks>
/// Rows are virtualised, because a long session holds thousands and a
/// non-virtualised list spends its time measuring rows nobody is looking at.
///
/// It declares no automation id. The identity the baseline uses for a scrolling
/// region (<c>class:scroll</c>) occurs three times there — the rail, the pane and
/// the side panel — and the comparison pairs the n-th occurrence with the n-th
/// control in tree order. Naming this one the same thing would make it the first
/// occurrence in this shell while the baseline's first is the rail's, so the two
/// would be compared against the wrong boxes. The rows inside it carry their own
/// ids, which is where the comparison has something to say.
/// </remarks>
public class StudioTranscript : ContentControl
{
}
