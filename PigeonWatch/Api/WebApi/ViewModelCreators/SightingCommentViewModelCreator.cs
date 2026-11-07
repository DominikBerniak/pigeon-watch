using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class SightingCommentViewModelCreator : ISightingCommentViewModelCreator
{
    public SightingCommentModel Create(SightingComment comment, Guid currentUserId)
    {
        if (comment.DeletedAt is not null)
        {
            return new()
            {
                Id = comment.Id,
                Text = null,
                AuthorDisplayName = null,
                CreatedAt = comment.CreatedAt,
                EditedAt = null,
                IsDeleted = true,
                IsMine = false
            };
        }

        return new()
        {
            Id = comment.Id,
            Text = comment.Text,
            AuthorDisplayName = comment.AuthorDisplayName,
            CreatedAt = comment.CreatedAt,
            EditedAt = comment.EditedAt,
            IsDeleted = false,
            IsMine = comment.AuthorId == currentUserId
        };
    }

    public SightingCommentPageModel CreatePage(SightingCommentPage page, Guid currentUserId)
    {
        return new()
        {
            Items = page.Items.Select(comment => Create(comment, currentUserId)).ToList(),
            TotalCount = page.TotalCount
        };
    }
}
