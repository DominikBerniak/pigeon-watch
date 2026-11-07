namespace PigeonWatch.BusinessObjects;

public static class SightingCommentErrorCodes
{
    public const string CooldownActive = SightingErrorCodes.CooldownActive;
    public const string CommentTextRequired = "CommentTextRequired";
    public const string CommentTextTooLong = "CommentTextTooLong";
    public const string SightingNotFound = "SightingNotFound";
    public const string CommentNotFound = "CommentNotFound";
    public const string NotCommentAuthor = "NotCommentAuthor";
    public const string AuthorNotFound = "AuthorNotFound";
    public const string InvalidPage = SightingErrorCodes.InvalidPage;
}
