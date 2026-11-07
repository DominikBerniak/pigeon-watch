using System.Reflection;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingThreadViewModelCreatorTests
{
    private static readonly DateTime lastMessageAt = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly SightingThreadViewModelCreator creator = new();
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly Guid participantId = Guid.NewGuid();

    [Fact]
    public void CreateThread_shows_the_participant_to_the_owner()
    {
        SightingThread thread = Thread();

        SightingThreadModel model = creator.CreateThread(thread, ownerId);

        Assert.Equal(thread.Id, model.Id);
        Assert.Equal(thread.SightingId, model.SightingId);
        Assert.Equal("Grey", model.SightingName);
        Assert.Equal("Bob", model.CounterpartDisplayName);
        Assert.True(model.IsOwner);
        Assert.Equal(lastMessageAt, model.LastMessageAt);
    }

    [Fact]
    public void CreateThread_shows_the_owner_to_the_participant()
    {
        SightingThreadModel model = creator.CreateThread(Thread(), participantId);

        Assert.Equal("Alice", model.CounterpartDisplayName);
        Assert.False(model.IsOwner);
    }

    [Fact]
    public void CreateMessage_marks_only_the_senders_message_as_mine()
    {
        SightingMessage message = new(Guid.NewGuid(), Guid.NewGuid(), participantId, "Bob", "Hello", lastMessageAt);

        SightingMessageModel mine = creator.CreateMessage(message, participantId);
        SightingMessageModel other = creator.CreateMessage(message, ownerId);

        Assert.Equal(message.Id, mine.Id);
        Assert.Equal("Hello", mine.Text);
        Assert.Equal("Bob", mine.SenderDisplayName);
        Assert.Equal(lastMessageAt, mine.CreatedAt);
        Assert.True(mine.IsMine);
        Assert.False(other.IsMine);
    }

    [Fact]
    public void Pages_map_every_item_in_order_and_keep_the_total_count()
    {
        SightingThread first = Thread();
        SightingThread second = Thread();
        SightingMessage ownerMessage = new(Guid.NewGuid(), first.Id, ownerId, "Alice", "Hi", lastMessageAt);
        SightingMessage participantMessage = new(Guid.NewGuid(), first.Id, participantId, "Bob", "Hello", lastMessageAt);

        SightingThreadPageModel threads = creator.CreateThreadPage(new SightingThreadPage([first, second], 42), ownerId);
        SightingMessagePageModel messages = creator.CreateMessagePage(new SightingMessagePage([ownerMessage, participantMessage], 7), ownerId);

        Assert.Equal([first.Id, second.Id], threads.Items.Select(item => item.Id));
        Assert.Equal(42, threads.TotalCount);
        Assert.Equal([ownerMessage.Id, participantMessage.Id], messages.Items.Select(item => item.Id));
        Assert.Equal([true, false], messages.Items.Select(item => item.IsMine));
        Assert.Equal(7, messages.TotalCount);
    }

    [Fact]
    public void CreateStart_combines_the_thread_and_the_message_for_the_caller()
    {
        SightingThread thread = Thread();
        SightingMessage message = new(Guid.NewGuid(), thread.Id, participantId, "Bob", "Hello", lastMessageAt);

        SightingThreadStartModel model = creator.CreateStart(thread, message, participantId);

        Assert.Equal(thread.Id, model.Thread.Id);
        Assert.Equal("Alice", model.Thread.CounterpartDisplayName);
        Assert.Equal(message.Id, model.Message.Id);
        Assert.True(model.Message.IsMine);
    }

    [Theory]
    [InlineData(typeof(SightingThreadModel))]
    [InlineData(typeof(SightingMessageModel))]
    [InlineData(typeof(SightingThreadStartModel))]
    [InlineData(typeof(SightingThreadPageModel))]
    [InlineData(typeof(SightingMessagePageModel))]
    public void Messaging_models_expose_no_user_id_email_or_audit_column(Type modelType)
    {
        PropertyInfo[] properties = modelType.GetProperties();

        Assert.DoesNotContain(properties, property => property.Name.Contains("OwnerId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("ParticipantId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("SenderId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("User", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("CreateDate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("UpdateDate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.PropertyType.Assembly == typeof(SightingThread).Assembly);
    }

    private SightingThread Thread()
    {
        return new(Guid.NewGuid(), Guid.NewGuid(), "Grey", ownerId, "Alice", participantId, "Bob", lastMessageAt);
    }
}
