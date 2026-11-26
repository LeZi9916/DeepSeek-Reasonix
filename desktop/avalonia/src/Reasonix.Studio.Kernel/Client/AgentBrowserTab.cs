namespace Reasonix.Studio.Kernel.Client;

/// <summary>Page metadata for a tab in the session's browser.</summary>
/// <param name="Target">The browser's stable identity for this page.</param>
/// <param name="Url">The browser-reported address.</param><param name="Title">The browser-reported title.</param>
/// <param name="Active">Whether the running agent has this tab selected.</param>
public sealed record AgentBrowserTab(string Target, string Url, string Title, bool Active);
