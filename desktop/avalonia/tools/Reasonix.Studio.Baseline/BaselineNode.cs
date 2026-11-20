using System.Text.Json.Serialization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// One element of a page domain, with the identity a replacement shell must carry
/// and the values it must reproduce.
/// </summary>
public sealed class BaselineNode
{
    /// <summary>
    /// Gets the structural path the collector recorded, kept for reading a diff.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the element's tag name.
    /// </summary>
    [JsonPropertyName("tag")]
    public string Tag
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the element's class attribute.
    /// </summary>
    [JsonPropertyName("cls")]
    public string Classes
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the value of the element's <c>data-action</c> attribute, when it has one.
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action
    {
        get; init;
    }

    /// <summary>
    /// Gets the element's ARIA role, when it carries one.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role
    {
        get; init;
    }

    /// <summary>
    /// Gets this node's occurrence among the nodes sharing its identity, in
    /// document order.
    /// </summary>
    /// <remarks>
    /// An identity is not unique — twenty labels share one class — so the
    /// occurrence is what makes a comparison key address exactly one element.
    /// </remarks>
    [JsonPropertyName("o")]
    public int Occurrence
    {
        get; init;
    }

    /// <summary>
    /// Gets the element's text, with dates and elapsed times replaced by placeholders.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the element's box.
    /// </summary>
    [JsonPropertyName("box")]
    public BaselineBox Box
    {
        get; init;
    } = new BaselineBox();

    /// <summary>
    /// Gets the index into the domain's style table that holds this element's computed style.
    /// </summary>
    [JsonPropertyName("s")]
    public int Style
    {
        get; init;
    }
}
