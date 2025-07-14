namespace Common.Enums;

/// <summary>
/// Well known assistant names used across the application.
/// Additional assistants can be added through configuration
/// without updating the code.
/// </summary>
public static class AssistantType
{
    public const string ReviewGood = "ReviewGood";
    public const string ReviewBad = "ReviewBad";
    public const string Questions_Others = "Questions_Others";
    public const string ChatGeneral = "ChatGeneral";

    /// <summary>
    /// Helper to build dynamic question assistant names.
    /// </summary>
    public static string Questions(string articlePrefix) => $"Questions_{articlePrefix}";
}
