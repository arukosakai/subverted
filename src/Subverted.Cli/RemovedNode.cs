using Subverted.Core;
using Subverted.Frontend;

namespace Subverted.Cli;

/// <summary>One node a removal reaches, with what <see cref="DeletionLoss"/> says becomes of it.</summary>
public sealed record RemovedNode(WorkingCopyEntry Entry, DeletionLine Loss);
