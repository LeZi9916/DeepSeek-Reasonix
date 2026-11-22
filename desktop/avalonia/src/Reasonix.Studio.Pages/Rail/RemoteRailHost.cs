using Reasonix.Studio.Contracts.Tree;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>A machine's current address-book entry and acknowledged project listing.</summary>
public sealed record RemoteRailHost
{
    /// <summary>Gets the address-book identity.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the displayed connection target.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Gets the link's canonical status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the canonical bootstrap step.</summary>
    public string Step { get; init; } = string.Empty;

    /// <summary>Gets the link's diagnostic detail.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Gets the projects supplied by the host or saved address book.</summary>
    public IReadOnlyList<TreeWorkspace> Projects { get; init; } = Array.Empty<TreeWorkspace>();
}
