namespace Subverted.Svn;

/// <summary>What a process has to do so the <c>svn</c> it starts writes its paths in UTF-8.</summary>
internal enum Utf8ConsoleStep
{
    Nothing,
    SwitchCodePages,
    AllocateWindowlessConsole,
}
