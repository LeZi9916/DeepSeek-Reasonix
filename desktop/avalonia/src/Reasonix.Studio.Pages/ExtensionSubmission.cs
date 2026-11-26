namespace Reasonix.Studio.Pages;

/// <summary>The reader's submission of an extension form.</summary>
internal enum ExtensionSubmission
{
    /// <summary>The form can be edited.</summary>
    Editable,

    /// <summary>The kernel has yet to acknowledge the form.</summary>
    Pending,

    /// <summary>The kernel accepted the submitted values.</summary>
    Accepted,
}
