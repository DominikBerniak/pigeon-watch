namespace PigeonWatch.Data.Identity;

public class ApplicationRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
