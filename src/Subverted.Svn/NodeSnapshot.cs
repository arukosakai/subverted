namespace Subverted.Svn;

/// <summary>
/// What the filesystem currently reports for a node. Absence is modelled by a null reference and
/// the file/directory split by the type itself, so a directory can never be handed size and mtime
/// fields that mean nothing for it.
/// </summary>
internal abstract record NodeSnapshot;
