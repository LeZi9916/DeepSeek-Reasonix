using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Events;

/// <summary>The choices submitted for one question in an ask request.</summary>
/// <param name="QuestionId">ID supplied by the kernel.</param>
/// <param name="Selected">Option labels and optional free text in selection order.</param>
public sealed record QuestionSelection(
    [property: JsonPropertyName("QuestionID")] string QuestionId,
    [property: JsonPropertyName("Selected")] IReadOnlyList<string> Selected);
