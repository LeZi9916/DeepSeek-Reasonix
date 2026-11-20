namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// One of the shell's own processes, as a report about memory may carry it.
/// </summary>
/// <param name="Type">What the process is: the main process, a renderer, a utility.</param>
/// <param name="PrivateBytes">The memory that is this process's own, in bytes.</param>
/// <param name="WorkingSetBytes">The memory the process currently has resident, in bytes.</param>
public sealed record ProcessMemory(string Type, long PrivateBytes, long WorkingSetBytes);
