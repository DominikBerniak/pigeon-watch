using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data;
using PigeonWatch.Data.Auditing;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public sealed class SightingThreadRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly UpperInvariantLookupNormalizer normalizer = new();
    private readonly ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingThreadRepositoryTests()
    {
        connection.Open();
        using PigeonWatchDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Get_sighting_owner_returns_the_reporter_or_null_for_an_unknown_sighting()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid sightingId = await CreateSightingAsync(ownerId);

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);

        Assert.Equal(ownerId, await repository.GetSightingOwnerAsync(sightingId, cancellationToken));
        Assert.Null(await repository.GetSightingOwnerAsync(Guid.NewGuid(), cancellationToken));
    }

    [Fact]
    public async Task Start_creates_the_thread_and_its_first_message_with_display_names_and_utc_times()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId, "Grey pigeon");
        DateTime now = timeProvider.UtcNow.UtcDateTime;

        (SightingThread Thread, SightingMessage Message)? started = await StartAsync(sightingId, participantId, "Can I help?", now);

        Assert.NotNull(started);
        SightingThread thread = started.Value.Thread;
        SightingMessage message = started.Value.Message;
        Assert.NotEqual(Guid.Empty, thread.Id);
        Assert.Equal(sightingId, thread.SightingId);
        Assert.Equal("Grey pigeon", thread.SightingName);
        Assert.Equal(ownerId, thread.OwnerId);
        Assert.Equal("Alice", thread.OwnerDisplayName);
        Assert.Equal(participantId, thread.ParticipantId);
        Assert.Equal("Bob", thread.ParticipantDisplayName);
        Assert.Equal(now, thread.LastMessageAt);
        Assert.Equal(DateTimeKind.Utc, thread.LastMessageAt.Kind);
        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(thread.Id, message.ThreadId);
        Assert.Equal(participantId, message.SenderId);
        Assert.Equal("Bob", message.SenderDisplayName);
        Assert.Equal("Can I help?", message.Text);
        Assert.Equal(now, message.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, message.CreatedAt.Kind);

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(thread, await CreateRepository(db).FindThreadAsync(sightingId, participantId, cancellationToken));
    }

    [Fact]
    public async Task Start_reuses_the_existing_thread_of_the_same_participant_and_moves_its_last_message_time()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);
        SightingThread first = (await StartAsync(sightingId, participantId, "First", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        DateTime later = timeProvider.UtcNow.UtcDateTime;

        (SightingThread Thread, SightingMessage Message)? second = await StartAsync(sightingId, participantId, "Second", later);

        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Value.Thread.Id);
        Assert.Equal(later, second.Value.Thread.LastMessageAt);
        Assert.Equal(first.Id, second.Value.Message.ThreadId);

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(1, await CountAsync(db, "SIGHTING_THREAD"));
        Assert.Equal(2, await CountAsync(db, "SIGHTING_MESSAGE"));
    }

    [Fact]
    public async Task Start_returns_null_and_stores_nothing_when_the_participant_or_the_sighting_does_not_exist()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);

        Assert.Null(await StartAsync(sightingId, Guid.NewGuid(), "Ghost", timeProvider.UtcNow.UtcDateTime));
        Assert.Null(await StartAsync(Guid.NewGuid(), participantId, "Nowhere", timeProvider.UtcNow.UtcDateTime));

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(0, await CountAsync(db, "SIGHTING_THREAD"));
        Assert.Equal(0, await CountAsync(db, "SIGHTING_MESSAGE"));
    }

    [Fact]
    public async Task The_unique_index_rejects_a_second_thread_for_the_same_sighting_and_participant()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);
        await StartAsync(sightingId, participantId, "First", timeProvider.UtcNow.UtcDateTime);

        await using PigeonWatchDbContext db = CreateContext();
        db.SightingThreads.Add(new SightingThreadEntity
        {
            SightingId = sightingId,
            ParticipantId = participantId,
            LastMessageAt = timeProvider.UtcNow.UtcDateTime
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task Start_that_loses_the_unique_index_race_appends_to_the_winning_thread()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);
        DateTime now = timeProvider.UtcNow.UtcDateTime;
        SightingThread? winner = null;
        ConcurrentStartInterceptor interceptor = new(async () =>
        {
            winner = (await StartAsync(sightingId, participantId, "Winner", now))!.Value.Thread;

            return new DbUpdateException(
                "Duplicate thread.",
                CreateSqlException(2601, "Cannot insert duplicate key row in object 'dbo.SIGHTING_THREAD' with unique index 'IX_SIGHTING_THREAD_SIGHTING_ID_PARTICIPANT_ID'."));
        });

        await using (PigeonWatchDbContext racingDb = CreateContext(interceptor))
        {
            (SightingThread Thread, SightingMessage Message)? started = await CreateRepository(racingDb)
                .StartOrAppendAsync(sightingId, participantId, "Loser", now, cancellationToken);

            Assert.NotNull(started);
            Assert.NotNull(winner);
            Assert.Equal(winner.Id, started.Value.Thread.Id);
            Assert.Equal(winner.Id, started.Value.Message.ThreadId);
            Assert.Equal("Loser", started.Value.Message.Text);
        }

        await using PigeonWatchDbContext db = CreateContext();
        SightingMessagePage messages = await CreateRepository(db).ListMessagesAsync(sightingId, winner.Id, participantId, 1, SightingMessageRules.PageSize, cancellationToken);

        Assert.Equal(1, await CountAsync(db, "SIGHTING_THREAD"));
        Assert.Equal(2, messages.TotalCount);
        Assert.Equal(["Loser", "Winner"], messages.Items.Select(message => message.Text).Order());
    }

    [Fact]
    public async Task Start_does_not_swallow_a_duplicate_key_on_another_index()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);
        ConcurrentStartInterceptor interceptor = new(() =>
        {
            return Task.FromResult(new DbUpdateException(
                "Duplicate elsewhere.",
                CreateSqlException(2601, "Cannot insert duplicate key row in object 'dbo.OTHER' with unique index 'IX_OTHER'.")));
        });

        await using PigeonWatchDbContext racingDb = CreateContext(interceptor);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateRepository(racingDb)
            .StartOrAppendAsync(sightingId, participantId, "Text", timeProvider.UtcNow.UtcDateTime, cancellationToken));
    }

    [Fact]
    public async Task Get_thread_for_user_returns_the_thread_to_the_participant_and_the_owner_only()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid outsiderId = await CreateUserAsync("outsider@example.com", "Carol");
        Guid sightingId = await CreateSightingAsync(ownerId);
        Guid otherSightingId = await CreateSightingAsync(ownerId);
        SightingThread thread = (await StartAsync(sightingId, participantId, "Hello", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);

        Assert.Equal(thread, await repository.GetThreadForUserAsync(sightingId, thread.Id, participantId, cancellationToken));
        Assert.Equal(thread, await repository.GetThreadForUserAsync(sightingId, thread.Id, ownerId, cancellationToken));
        Assert.Null(await repository.GetThreadForUserAsync(sightingId, thread.Id, outsiderId, cancellationToken));
        Assert.Null(await repository.GetThreadForUserAsync(otherSightingId, thread.Id, participantId, cancellationToken));
        Assert.Null(await repository.GetThreadForUserAsync(sightingId, Guid.NewGuid(), participantId, cancellationToken));
    }

    [Fact]
    public async Task Find_thread_returns_only_the_thread_of_that_sighting_and_participant()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid outsiderId = await CreateUserAsync("outsider@example.com", "Carol");
        Guid sightingId = await CreateSightingAsync(ownerId);
        Guid otherSightingId = await CreateSightingAsync(ownerId);
        SightingThread thread = (await StartAsync(sightingId, participantId, "Hello", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);

        Assert.Equal(thread, await repository.FindThreadAsync(sightingId, participantId, cancellationToken));
        Assert.Null(await repository.FindThreadAsync(sightingId, outsiderId, cancellationToken));
        Assert.Null(await repository.FindThreadAsync(sightingId, ownerId, cancellationToken));
        Assert.Null(await repository.FindThreadAsync(otherSightingId, participantId, cancellationToken));
    }

    [Fact]
    public async Task Append_by_the_owner_or_the_participant_stores_the_message_and_moves_the_last_message_time()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);
        SightingThread thread = (await StartAsync(sightingId, participantId, "Hello", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(1);
        DateTime replyTime = timeProvider.UtcNow.UtcDateTime;

        SightingMessage? reply = await AppendAsync(sightingId, thread.Id, ownerId, "Thanks", replyTime);

        Assert.NotNull(reply);
        Assert.Equal(thread.Id, reply.ThreadId);
        Assert.Equal(ownerId, reply.SenderId);
        Assert.Equal("Alice", reply.SenderDisplayName);
        Assert.Equal("Thanks", reply.Text);
        Assert.Equal(replyTime, reply.CreatedAt);

        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(1);
        DateTime followUpTime = timeProvider.UtcNow.UtcDateTime;
        SightingMessage? followUp = await AppendAsync(sightingId, thread.Id, participantId, "On my way", followUpTime);

        Assert.NotNull(followUp);
        Assert.Equal("Bob", followUp.SenderDisplayName);

        await using PigeonWatchDbContext db = CreateContext();
        SightingThread? reloaded = await CreateRepository(db).FindThreadAsync(sightingId, participantId, cancellationToken);

        Assert.Equal(followUpTime, reloaded!.LastMessageAt);
        Assert.Equal(3, await CountAsync(db, "SIGHTING_MESSAGE"));
    }

    [Fact]
    public async Task Append_returns_null_and_writes_nothing_for_an_outsider_another_sighting_an_unknown_thread_or_a_missing_sender()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid outsiderId = await CreateUserAsync("outsider@example.com", "Carol");
        Guid sightingId = await CreateSightingAsync(ownerId);
        Guid otherSightingId = await CreateSightingAsync(ownerId);
        SightingThread thread = (await StartAsync(sightingId, participantId, "Hello", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;
        DateTime later = timeProvider.UtcNow.UtcDateTime.AddMinutes(1);

        Assert.Null(await AppendAsync(sightingId, thread.Id, outsiderId, "Let me in", later));
        Assert.Null(await AppendAsync(otherSightingId, thread.Id, participantId, "Wrong sighting", later));
        Assert.Null(await AppendAsync(sightingId, Guid.NewGuid(), participantId, "Unknown thread", later));
        Assert.Null(await AppendAsync(sightingId, thread.Id, Guid.NewGuid(), "Deleted user", later));

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(1, await CountAsync(db, "SIGHTING_MESSAGE"));
        Assert.Equal(thread, await CreateRepository(db).FindThreadAsync(sightingId, participantId, cancellationToken));
    }

    [Fact]
    public async Task List_threads_for_sighting_returns_all_threads_to_the_owner_the_own_thread_to_a_participant_and_none_to_an_outsider()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        Guid daveId = await CreateUserAsync("dave@example.com", "Dave");
        Guid outsiderId = await CreateUserAsync("outsider@example.com", "Carol");
        Guid sightingId = await CreateSightingAsync(ownerId);
        Guid otherSightingId = await CreateSightingAsync(ownerId);
        SightingThread bobThread = (await StartAsync(sightingId, bobId, "Bob here", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;
        SightingThread daveThread = (await StartAsync(sightingId, daveId, "Dave here", timeProvider.UtcNow.UtcDateTime.AddMinutes(1)))!.Value.Thread;
        await StartAsync(otherSightingId, outsiderId, "Elsewhere", timeProvider.UtcNow.UtcDateTime.AddMinutes(2));

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);
        SightingThreadPage ownerPage = await repository.ListThreadsForSightingAsync(sightingId, ownerId, 1, SightingMessageRules.PageSize, cancellationToken);
        SightingThreadPage bobPage = await repository.ListThreadsForSightingAsync(sightingId, bobId, 1, SightingMessageRules.PageSize, cancellationToken);
        SightingThreadPage outsiderPage = await repository.ListThreadsForSightingAsync(sightingId, outsiderId, 1, SightingMessageRules.PageSize, cancellationToken);

        Assert.Equal([daveThread.Id, bobThread.Id], ownerPage.Items.Select(thread => thread.Id));
        Assert.Equal(2, ownerPage.TotalCount);
        Assert.Equal([bobThread.Id], bobPage.Items.Select(thread => thread.Id));
        Assert.Equal(1, bobPage.TotalCount);
        Assert.Empty(outsiderPage.Items);
        Assert.Equal(0, outsiderPage.TotalCount);
    }

    [Fact]
    public async Task List_threads_for_user_returns_threads_where_the_user_is_participant_or_owner_ordered_by_last_message()
    {
        Guid aliceId = await CreateUserAsync("alice@example.com", "Alice");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        Guid outsiderId = await CreateUserAsync("outsider@example.com", "Carol");
        Guid aliceSightingId = await CreateSightingAsync(aliceId);
        Guid bobSightingId = await CreateSightingAsync(bobId);
        DateTime start = timeProvider.UtcNow.UtcDateTime;
        SightingThread bobOnAlice = (await StartAsync(aliceSightingId, bobId, "Bob to Alice", start))!.Value.Thread;
        SightingThread aliceOnBob = (await StartAsync(bobSightingId, aliceId, "Alice to Bob", start.AddMinutes(1)))!.Value.Thread;
        await AppendAsync(aliceSightingId, bobOnAlice.Id, aliceId, "Reply", start.AddMinutes(2));

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);
        SightingThreadPage alicePage = await repository.ListThreadsForUserAsync(aliceId, 1, SightingMessageRules.PageSize, cancellationToken);
        SightingThreadPage bobPage = await repository.ListThreadsForUserAsync(bobId, 1, SightingMessageRules.PageSize, cancellationToken);
        SightingThreadPage outsiderPage = await repository.ListThreadsForUserAsync(outsiderId, 1, SightingMessageRules.PageSize, cancellationToken);

        Assert.Equal([bobOnAlice.Id, aliceOnBob.Id], alicePage.Items.Select(thread => thread.Id));
        Assert.Equal(2, alicePage.TotalCount);
        Assert.Equal([bobOnAlice.Id, aliceOnBob.Id], bobPage.Items.Select(thread => thread.Id));
        Assert.Empty(outsiderPage.Items);
        Assert.Equal(0, outsiderPage.TotalCount);
    }

    [Fact]
    public async Task List_threads_breaks_a_last_message_tie_by_id_descending_and_pages()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid sightingId = await CreateSightingAsync(ownerId);
        DateTime now = timeProvider.UtcNow.UtcDateTime;
        List<Guid> threadIds = [];

        for (int index = 0; index < 3; index++)
        {
            Guid participantId = await CreateUserAsync($"participant{index}@example.com", $"Helper{index}");
            threadIds.Add((await StartAsync(sightingId, participantId, "Hi", now))!.Value.Thread.Id);
        }

        Guid[] expected = threadIds.OrderDescending().ToArray();

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);
        SightingThreadPage first = await repository.ListThreadsForUserAsync(ownerId, 1, 2, cancellationToken);
        SightingThreadPage second = await repository.ListThreadsForSightingAsync(sightingId, ownerId, 2, 2, cancellationToken);
        SightingThreadPage overflow = await repository.ListThreadsForUserAsync(ownerId, int.MaxValue, SightingMessageRules.PageSize, cancellationToken);

        Assert.Equal(expected.Take(2), first.Items.Select(thread => thread.Id));
        Assert.Equal(expected.Skip(2), second.Items.Select(thread => thread.Id));
        Assert.Equal(3, first.TotalCount);
        Assert.Empty(overflow.Items);
        Assert.Equal(3, overflow.TotalCount);
    }

    [Fact]
    public async Task List_messages_returns_the_newest_first_with_an_id_tiebreak_and_pages_without_overflow()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid sightingId = await CreateSightingAsync(ownerId);
        (SightingThread Thread, SightingMessage Message) started = (await StartAsync(sightingId, participantId, "One", timeProvider.UtcNow.UtcDateTime))!.Value;
        SightingMessage tied = (await AppendAsync(sightingId, started.Thread.Id, ownerId, "Two", timeProvider.UtcNow.UtcDateTime))!;
        Guid[] tiedIds = new[] { started.Message.Id, tied.Id }.OrderDescending().ToArray();
        List<Guid> newerFirst = [];

        for (int index = 0; index < 3; index++)
        {
            timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(1);
            SightingMessage message = (await AppendAsync(sightingId, started.Thread.Id, participantId, $"Later {index}", timeProvider.UtcNow.UtcDateTime))!;
            newerFirst.Insert(0, message.Id);
        }

        Guid[] expected = [.. newerFirst, .. tiedIds];

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);
        SightingMessagePage all = await repository.ListMessagesAsync(sightingId, started.Thread.Id, ownerId, 1, SightingMessageRules.PageSize, cancellationToken);
        SightingMessagePage second = await repository.ListMessagesAsync(sightingId, started.Thread.Id, participantId, 2, 2, cancellationToken);
        SightingMessagePage third = await repository.ListMessagesAsync(sightingId, started.Thread.Id, participantId, 3, 2, cancellationToken);
        SightingMessagePage pastTheEnd = await repository.ListMessagesAsync(sightingId, started.Thread.Id, participantId, 4, 2, cancellationToken);
        SightingMessagePage overflow = await repository.ListMessagesAsync(sightingId, started.Thread.Id, participantId, int.MaxValue, SightingMessageRules.PageSize, cancellationToken);

        Assert.Equal(expected, all.Items.Select(message => message.Id));
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(expected.Skip(2).Take(2), second.Items.Select(message => message.Id));
        Assert.Equal(expected.Skip(4), third.Items.Select(message => message.Id));
        Assert.Empty(pastTheEnd.Items);
        Assert.Equal(5, pastTheEnd.TotalCount);
        Assert.Empty(overflow.Items);
        Assert.Equal(5, overflow.TotalCount);
    }

    [Fact]
    public async Task List_messages_returns_nothing_to_an_outsider_or_for_another_sighting()
    {
        Guid ownerId = await CreateUserAsync("owner@example.com", "Alice");
        Guid participantId = await CreateUserAsync("participant@example.com", "Bob");
        Guid outsiderId = await CreateUserAsync("outsider@example.com", "Carol");
        Guid sightingId = await CreateSightingAsync(ownerId);
        Guid otherSightingId = await CreateSightingAsync(ownerId);
        SightingThread thread = (await StartAsync(sightingId, participantId, "Private", timeProvider.UtcNow.UtcDateTime))!.Value.Thread;

        await using PigeonWatchDbContext db = CreateContext();
        ISightingThreadRepository repository = CreateRepository(db);
        SightingMessagePage outsiderPage = await repository.ListMessagesAsync(sightingId, thread.Id, outsiderId, 1, SightingMessageRules.PageSize, cancellationToken);
        SightingMessagePage otherSightingPage = await repository.ListMessagesAsync(otherSightingId, thread.Id, participantId, 1, SightingMessageRules.PageSize, cancellationToken);

        Assert.Empty(outsiderPage.Items);
        Assert.Equal(0, outsiderPage.TotalCount);
        Assert.Empty(otherSightingPage.Items);
        Assert.Equal(0, otherSightingPage.TotalCount);
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private async Task<(SightingThread Thread, SightingMessage Message)?> StartAsync(Guid sightingId, Guid participantId, string text, DateTime now)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).StartOrAppendAsync(sightingId, participantId, text, now, cancellationToken);
    }

    private async Task<SightingMessage?> AppendAsync(Guid sightingId, Guid threadId, Guid senderId, string text, DateTime now)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).AppendAsync(sightingId, threadId, senderId, text, now, cancellationToken);
    }

    private async Task<int> CountAsync(PigeonWatchDbContext db, string table)
    {
        return await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM {table}").SingleAsync(cancellationToken);
    }

    private async Task<Guid> CreateSightingAsync(Guid reporterId, string? name = null)
    {
        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = new SightingRepository(db, new SightingMapper());
        Sighting? created = await repository.CreateAsync(
            new NewSighting(reporterId, name, "Limping near the fountain", "Cannot fly", 52.0, 21.0, SightingStatus.Spotted, SightingCriticality.Urgent),
            cancellationToken);

        Assert.NotNull(created);

        return created.Id;
    }

    private async Task<Guid> CreateUserAsync(string email, string displayName)
    {
        ApplicationUser user = new()
        {
            Email = email,
            NormalizedEmail = normalizer.NormalizeEmail(email)!,
            UserName = email,
            NormalizedUserName = normalizer.NormalizeName(email)!,
            DisplayName = displayName,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchUserStore store = new(db, new UserAccountMapper(), normalizer, new IdentityErrorDescriber(), NullLogger<PigeonWatchUserStore>.Instance);
        IdentityResult result = await store.CreateAsync(user, cancellationToken);

        Assert.True(result.Succeeded);

        return user.Id;
    }

    private ISightingThreadRepository CreateRepository(PigeonWatchDbContext db)
    {
        return new SightingThreadRepository(db, new SightingThreadMapper());
    }

    private PigeonWatchDbContext CreateContext(ConcurrentStartInterceptor? concurrentStart = null)
    {
        DbContextOptionsBuilder<PigeonWatchDbContext> builder = new DbContextOptionsBuilder<PigeonWatchDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new HttpContextCurrentUserProvider(new HttpContextAccessor(), NullLogger<HttpContextCurrentUserProvider>.Instance),
                timeProvider));

        if (concurrentStart is not null)
            builder.AddInterceptors(concurrentStart);

        return new PigeonWatchDbContext(builder.Options);
    }

    private static SqlException CreateSqlException(int number, string message)
    {
        ConstructorInfo errorConstructor = typeof(SqlError)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(constructor => constructor.GetParameters().Length)
            .First();
        object?[] errorArguments = errorConstructor.GetParameters()
            .Select(parameter => ErrorArgument(parameter, number, message))
            .ToArray();
        SqlError error = (SqlError)errorConstructor.Invoke(errorArguments);

        SqlErrorCollection errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(errors, [error]);

        MethodInfo createException = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(SqlErrorCollection), typeof(string)])!;

        return (SqlException)createException.Invoke(null, [errors, "17.0.0"])!;
    }

    private static object? ErrorArgument(ParameterInfo parameter, int number, string message)
    {
        if (parameter.Name == "infoNumber")
            return number;

        if (parameter.Name == "errorMessage")
            return message;

        if (parameter.ParameterType == typeof(string))
            return string.Empty;

        return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
    }

    private sealed class ConcurrentStartInterceptor(Func<Task<DbUpdateException>> raceAsync) : SaveChangesInterceptor
    {
        private bool raced;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            bool insertsThread = eventData.Context!.ChangeTracker.Entries<SightingThreadEntity>().Any(entry => entry.State == EntityState.Added);

            if (raced || !insertsThread)
                return result;

            raced = true;

            throw await raceAsync();
        }
    }
}
