using System.Reflection;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingCommentViewModelCreatorTests
{
    private static readonly DateTime createdAt = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly SightingCommentViewModelCreator creator = new();
    private readonly Guid authorId = Guid.NewGuid();

    [Fact]
    public void Create_maps_a_live_comment_and_marks_it_as_mine_for_its_author()
    {
        SightingComment comment = Comment(editedAt: createdAt.AddMinutes(5));

        SightingCommentModel mine = creator.Create(comment, authorId);
        SightingCommentModel other = creator.Create(comment, Guid.NewGuid());

        Assert.Equal(comment.Id, mine.Id);
        Assert.Equal("Seen near the fountain", mine.Text);
        Assert.Equal("Pidgey", mine.AuthorDisplayName);
        Assert.Equal(createdAt, mine.CreatedAt);
        Assert.Equal(createdAt.AddMinutes(5), mine.EditedAt);
        Assert.False(mine.IsDeleted);
        Assert.True(mine.IsMine);
        Assert.False(other.IsMine);
    }

    [Fact]
    public void Create_blanks_a_deleted_comment_into_a_placeholder_even_for_its_author()
    {
        SightingComment comment = Comment(editedAt: createdAt.AddMinutes(5), deletedAt: createdAt.AddMinutes(10));

        SightingCommentModel model = creator.Create(comment, authorId);

        Assert.Equal(comment.Id, model.Id);
        Assert.Null(model.Text);
        Assert.Null(model.AuthorDisplayName);
        Assert.Null(model.EditedAt);
        Assert.Equal(createdAt, model.CreatedAt);
        Assert.True(model.IsDeleted);
        Assert.False(model.IsMine);
    }

    [Fact]
    public void CreatePage_maps_every_item_in_order_and_keeps_the_total_count()
    {
        SightingComment live = Comment();
        SightingComment deleted = Comment(deletedAt: createdAt.AddMinutes(1));

        SightingCommentPageModel model = creator.CreatePage(new SightingCommentPage([live, deleted], 42), authorId);

        Assert.Equal([live.Id, deleted.Id], model.Items.Select(item => item.Id));
        Assert.Equal([false, true], model.Items.Select(item => item.IsDeleted));
        Assert.Equal(42, model.TotalCount);
    }

    [Fact]
    public void Comment_model_exposes_no_author_id_email_or_audit_column()
    {
        PropertyInfo[] properties = typeof(SightingCommentModel).GetProperties();

        Assert.DoesNotContain(properties, property => property.Name.Contains("AuthorId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("User", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("SightingId", StringComparison.OrdinalIgnoreCase));
    }

    private SightingComment Comment(DateTime? editedAt = null, DateTime? deletedAt = null)
    {
        return new(Guid.NewGuid(), Guid.NewGuid(), authorId, "Pidgey", "Seen near the fountain", createdAt, editedAt, deletedAt);
    }
}
