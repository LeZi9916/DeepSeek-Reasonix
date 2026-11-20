using System.Text.Json;

namespace Reasonix.Studio.Contracts.Events;

/// <summary>
/// One frame the kernel published, carrying the number it was assigned.
/// </summary>
/// <param name="Seq">
/// The frame's position in the stream, or 0 when the frame is not resumable.
/// Only a numbered frame is remembered as a resume point.
/// </param>
/// <param name="Kind">The frame's <c>kind</c>, read once so a filter need not re-parse it.</param>
/// <param name="Payload">The frame's whole JSON body.</param>
/// <remarks>
/// It lives in the contract layer rather than beside the transport because both
/// sides read it: the transport produces frames and the reducer folds them, and
/// neither should have to depend on the other to name one.
/// </remarks>
public readonly record struct WireFrame(long Seq, string Kind, JsonElement Payload)
{
    /// <summary>
    /// Gets a value indicating whether the frame occupies a position a client can
    /// resume from.
    /// </summary>
    public bool IsNumbered => Seq > 0;

    /// <summary>
    /// Reads the two fields the subscription itself needs, leaving the rest of the
    /// body unread until a reducer asks for it.
    /// </summary>
    /// <param name="json">One frame's JSON text.</param>
    /// <param name="frame">The parsed frame, when the text was a JSON object.</param>
    /// <returns>True when the text parsed into a frame.</returns>
    public static bool TryParse(string json, out WireFrame frame)
    {
        frame = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var kind = string.Empty;
            if (root.TryGetProperty("kind", out var kindValue) && kindValue.ValueKind == JsonValueKind.String)
            {
                kind = kindValue.GetString() ?? string.Empty;
            }

            // Absent or non-numeric seq is not an error: a live-only frame simply
            // is not a resume point, and demanding a number would drop frames the
            // kernel never promised to number.
            var seq = 0L;
            if (root.TryGetProperty("seq", out var seqValue) && seqValue.ValueKind == JsonValueKind.Number)
            {
                seq = seqValue.TryGetInt64(out var parsed) ? parsed : 0L;
            }

            frame = new WireFrame(seq, kind, root.Clone());
            return true;
        }
        catch (JsonException)
        {
            // A malformed frame must not tear down the stream, the same rule the
            // browser client states for its own parse.
            return false;
        }
    }
}
