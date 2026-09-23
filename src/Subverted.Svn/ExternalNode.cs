using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// An <c>svn:externals</c> checkout registered in this working copy's <c>EXTERNALS</c> table.
/// </summary>
/// <param name="Kind">
/// Directory for the usual case; SVN also allows a single-file external, which has no subtree to
/// suppress.
/// </param>
internal readonly record struct ExternalNode(string RelPath, NodeKind Kind);
