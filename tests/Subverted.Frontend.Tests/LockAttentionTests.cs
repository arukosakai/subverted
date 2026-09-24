using Subverted.Protocol;

namespace Subverted.Frontend.Tests;

public sealed class LockAttentionTests
{
    private const string Held =
        "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'rena' in filesystem '/repo/db'";

    private const string Gone = "svn: warning: W160040: No lock on path '/art/hero.png'";

    [Test]
    public async Task A_lock_with_a_refusal_needs_attention_even_beside_one_that_was_granted()
    {
        var response = new LockResponse("'a.png' locked by user 'keiichi'.", [Held]);

        await Assert.That(LockAttention.IsNeeded(response)).IsTrue();
    }

    [Test]
    [Arguments("'a.png' locked by user 'keiichi'.")]
    [Arguments("")]
    public async Task A_lock_with_no_refusal_does_not_need_attention(string notifications)
    {
        await Assert.That(LockAttention.IsNeeded(new LockResponse(notifications, []))).IsFalse();
    }

    [Test]
    public async Task An_unlock_whose_lock_had_already_gone_needs_attention()
    {
        await Assert.That(LockAttention.IsNeeded(new UnlockResponse("", [Gone]))).IsTrue();
    }

    [Test]
    public async Task An_unlock_with_no_refusal_does_not_need_attention()
    {
        await Assert
            .That(LockAttention.IsNeeded(new UnlockResponse("'a.png' unlocked.", [])))
            .IsFalse();
    }
}
