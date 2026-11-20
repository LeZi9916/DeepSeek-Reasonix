using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The conversation itself: every row the session has produced, in order.
/// </summary>
/// <remarks>
/// Rows are virtualised, because a long session holds thousands and a
/// non-virtualised list spends its time measuring rows nobody is looking at.
/// </remarks>
public class StudioTranscript : ContentControl
{
}
