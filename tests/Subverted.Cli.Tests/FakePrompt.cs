namespace Subverted.Cli.Tests;

/// <summary>
/// A terminal with the answers already typed. Running out of them is a failure rather than end of
/// input, so a loop that asks one question too many is a red test instead of a quiet <c>q</c>.
/// </summary>
internal sealed class FakePrompt(params string?[] answers) : IPrompt
{
    private readonly Queue<string?> _answers = new(answers);

    public List<string> Written { get; } = [];

    public void Write(string text) => Written.Add(text);

    public void WriteLine(string text) => Written.Add(text);

    public string? ReadLine() =>
        _answers.Count > 0
            ? _answers.Dequeue()
            : throw new InvalidOperationException(
                "The walk asked more questions than this test had answers for."
            );
}
