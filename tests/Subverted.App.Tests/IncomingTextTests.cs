using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class IncomingTextTests
{
    [Test]
    [Arguments(null, null)]
    [Arguments(0, null)]
    [Arguments(1, "1")]
    [Arguments(1234, "1,234")]
    public async Task The_badge_shows_only_when_something_is_waiting(int? incoming, string? badge)
    {
        await Assert.That(IncomingText.Badge(incoming)).IsEqualTo(badge);
    }

    [Test]
    [Arguments(null, null)]
    [Arguments(0, "Nothing new on the server")]
    [Arguments(1, "1 change on the server to bring in")]
    [Arguments(2, "2 changes on the server to bring in")]
    [Arguments(1234, "1,234 changes on the server to bring in")]
    public async Task The_sentence_says_what_the_server_answered(int? incoming, string? sentence)
    {
        await Assert.That(IncomingText.Sentence(incoming)).IsEqualTo(sentence);
    }
}
