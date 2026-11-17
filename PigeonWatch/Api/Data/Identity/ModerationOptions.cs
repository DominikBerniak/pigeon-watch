namespace PigeonWatch.Data.Identity;

public class ModerationOptions
{
    public const string SectionName = "Moderation";

    public List<string> AdminEmails { get; set; } = [];
}
