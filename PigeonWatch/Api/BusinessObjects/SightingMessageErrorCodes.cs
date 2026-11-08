namespace PigeonWatch.BusinessObjects;

public static class SightingMessageErrorCodes
{
    public const string SightingNotFound = SightingCommentErrorCodes.SightingNotFound;
    public const string ThreadNotFound = "ThreadNotFound";
    public const string OwnSightingContact = "OwnSightingContact";
    public const string MessageTextRequired = "MessageTextRequired";
    public const string MessageTextTooLong = "MessageTextTooLong";
    public const string SenderNotFound = "SenderNotFound";
    public const string InvalidPage = SightingErrorCodes.InvalidPage;
}
