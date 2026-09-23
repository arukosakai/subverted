namespace Subverted.Cli;

/// <summary>What was typed at the picker's prompt, as the decision it means.</summary>
public enum PickAnswer
{
    /// <summary>Send this node.</summary>
    Send,

    /// <summary>Leave this node local.</summary>
    Skip,

    /// <summary>Send this node and everything still to come, without asking again.</summary>
    SendRest,

    /// <summary>Stop asking and commit what has been picked so far.</summary>
    SkipRest,

    /// <summary>Stop, and commit nothing at all.</summary>
    Quit,

    /// <summary>Not a decision — say what the letters mean and ask the same question again.</summary>
    Explain,
}
