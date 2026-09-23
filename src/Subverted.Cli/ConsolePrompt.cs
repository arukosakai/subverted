namespace Subverted.Cli;

/// <summary>The real terminal behind <see cref="IPrompt"/>, and nothing else.</summary>
public sealed class ConsolePrompt : IPrompt
{
    public void Write(string text) => Console.Write(text);

    public void WriteLine(string text) => Console.WriteLine(text);

    public string? ReadLine() => Console.ReadLine();
}
