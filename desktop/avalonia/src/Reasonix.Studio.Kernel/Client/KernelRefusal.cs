using Reasonix.Studio.Contracts.Refusal;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// A request the kernel answered with a refusal, kept as the identity it carried.
/// </summary>
/// <remarks>
/// The status is the part a caller branches on. A 409 from <c>/resume</c> means a
/// turn owns the session, which is a question to put to the person; a 404 is a
/// transcript that is gone; a 413 is one too large to replay; a 400 is this
/// request's own shape. Collapsing them into one "the request failed" leaves a
/// window unable to say which, and leaves the reader unable to act.
///
/// A coded body names the reason more precisely than the status can. The kernel's
/// own <c>http.Error</c> call sites answer with plain text and no code, so that
/// text is kept as <see cref="Detail"/> rather than dropped: it is the only
/// account of that failure there is.
///
/// It implements <see cref="IRefusal"/> so a page can turn it into a sentence
/// without taking a reference on this assembly.
/// </remarks>
public sealed class KernelRefusal : Exception, IRefusal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KernelRefusal"/> class.
    /// </summary>
    /// <param name="path">The request's path, for a sentence that has nothing else.</param>
    /// <param name="status">The HTTP status the kernel answered with.</param>
    /// <param name="reason">The coded refusal, or null when the answer carried none.</param>
    /// <param name="detail">The plain-text account, or null when the body was coded.</param>
    public KernelRefusal(string? path, int status, Reason? reason, string? detail)
        : base(SentenceOf(path, status, reason, detail))
    {
        Status = status;
        Reason = reason;
        Detail = detail;
        Path = path;
    }

    /// <summary>
    /// Gets the HTTP status the kernel answered with.
    /// </summary>
    public int Status
    {
        get;
    }

    /// <summary>
    /// Gets the coded refusal, or null when the answer carried none.
    /// </summary>
    public Reason? Reason
    {
        get;
    }

    /// <summary>
    /// Gets the kernel's plain-text account, or null when the body was coded.
    /// </summary>
    public string? Detail
    {
        get;
    }

    /// <summary>
    /// Gets the request's path, or null when the answer does not say.
    /// </summary>
    public string? Path
    {
        get;
    }

    /// <summary>
    /// Gets the dotted code, or null when this refusal carries none.
    /// </summary>
    /// <remarks>
    /// Null is the answer, not a reason to start reading <see cref="Exception.Message"/>
    /// for one: a plain-text refusal has no identity beyond its status.
    /// </remarks>
    public string? Code => Reason?.Code;

    /// <summary>
    /// Reads a refusal from the answer that carries it.
    /// </summary>
    /// <param name="response">The unsuccessful answer.</param>
    /// <param name="cancellationToken">Cancels the body read.</param>
    /// <returns>The refusal, with whatever identity the body held.</returns>
    /// <remarks>
    /// The body is read once, as text, because not every refusal is an envelope:
    /// asking for JSON first would throw away the plain-text account and leave a
    /// reader with nothing but a status.
    /// </remarks>
    public static async Task<KernelRefusal> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var path = response.RequestMessage?.RequestUri?.PathAndQuery;
        var raw = string.Empty;
        try
        {
            raw = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A body that could not be read is worth no more than no body at all,
            // and the status is still the identity that arrived.
        }

        Reason? reason = null;
        string? detail = raw.Length == 0 ? null : raw;
        if (raw.StartsWith('{'))
        {
            try
            {
                reason = JsonSerializer.Deserialize<Reason>(raw);
            }
            catch (JsonException)
            {
                // An object that is not a refusal: its text is all there is.
            }

            if (reason is not null)
            {
                detail = null;
            }
        }

        return new KernelRefusal(path, (int)response.StatusCode, reason, detail);
    }

    /// <summary>
    /// Builds the fallback sentence, which is what a log and an untranslated code read.
    /// </summary>
    /// <param name="path">The request's path.</param>
    /// <param name="status">The HTTP status.</param>
    /// <param name="reason">The coded refusal, when there was one.</param>
    /// <param name="detail">The plain-text account, when there was one.</param>
    /// <returns>The kernel's English, then the plain text, then the status alone.</returns>
    private static string SentenceOf(string? path, int status, Reason? reason, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(reason?.Error))
        {
            return reason.Error;
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            return detail;
        }

        var number = status.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(path) ? "HTTP " + number : path + ": " + number;
    }
}
