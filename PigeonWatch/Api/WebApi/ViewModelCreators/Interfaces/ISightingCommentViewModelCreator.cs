using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface ISightingCommentViewModelCreator
{
    SightingCommentModel Create(SightingComment comment, Guid currentUserId);
    SightingCommentPageModel CreatePage(SightingCommentPage page, Guid currentUserId);
}
