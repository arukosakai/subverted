namespace Subverted.Svn;

/// <summary>One <c>&lt;path&gt;</c> of <c>svn diff --summarize --xml</c>, its attributes as svn wrote them.</summary>
/// <param name="Item">What happened to its content: <c>modified</c>, <c>added</c>, <c>deleted</c>, <c>none</c>.</param>
/// <param name="Props">What happened to its properties: <c>modified</c> or <c>none</c>.</param>
/// <param name="Kind"><c>file</c> or <c>dir</c>.</param>
internal sealed record SummarisedPath(string Item, string Props, string Kind)
{
    /// <summary>Only the file's text changed — the one shape whose diff is two texts and nothing else.</summary>
    public bool IsTextEditOfAFile => Item == "modified" && Props == "none" && Kind == "file";
}
