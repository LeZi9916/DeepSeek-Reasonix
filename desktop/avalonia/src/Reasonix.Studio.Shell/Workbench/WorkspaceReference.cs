using System;
using System.Linq;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Projects host file identities into the canonical reference grammar.</summary>
internal static class WorkspaceReference
{
    /// <summary>Escapes only separators consumed by the kernel's reference tokenizer.</summary>
    /// <param name="path">Workspace-relative entry.</param><returns>The reference or an unrepresentable empty value.</returns>
    internal static string Token(string path)
    {
        if (path.Length == 0 || path.Contains(':') || path.EndsWith('\\') || path.TrimEnd() != path || path.Any(c => c is '\r' or '\n' or '\f') || ".,;!?)}]".Contains(path[^1]))
        {
            return string.Empty;
        }

        return "@" + path.Replace(" ", "\\ ", StringComparison.Ordinal).Replace("\t", "\\\t", StringComparison.Ordinal);
    }

    /// <summary>Preserves the host's path dialect even when this window runs on another OS.</summary>
    /// <param name="root">Canonical workspace.</param><param name="path">Relative entry.</param><returns>The host-absolute path.</returns>
    internal static string Absolute(string root, string path)
    {
        var windows = root.StartsWith("\\\\", StringComparison.Ordinal) || (root.Length > 2 && char.IsAsciiLetter(root[0]) && root[1] == ':' && root[2] is '\\' or '/');
        if (!windows && !root.StartsWith('/'))
        {
            return string.Empty;
        }

        var separator = root.StartsWith("\\\\", StringComparison.Ordinal) || (windows && root[2] == '\\') ? '\\' : '/';
        return root.TrimEnd('/', '\\') + separator + (separator == '\\' ? path.Replace('/', '\\') : path);
    }

    /// <summary>Names the language using the same extension table as the file status bar.</summary>
    /// <param name="extension">Lowercase extension without a dot.</param><returns>The displayed language.</returns>
    internal static string Language(string extension) => extension switch
    {
        "go" => "Go",
        "py" => "Python",
        "ts" or "tsx" => "TypeScript",
        "js" or "jsx" => "JavaScript",
        "rs" => "Rust",
        "java" => "Java",
        "kt" => "Kotlin",
        "c" or "h" => "C",
        "cpp" => "C++",
        "cs" => "C#",
        "rb" => "Ruby",
        "php" => "PHP",
        "swift" => "Swift",
        "md" => "Markdown",
        "json" => "JSON",
        "yaml" or "yml" => "YAML",
        "toml" => "TOML",
        "css" => "CSS",
        "html" => "HTML",
        "sh" => "Shell",
        "sql" => "SQL",
        _ => extension.ToUpperInvariant(),
    };
}
