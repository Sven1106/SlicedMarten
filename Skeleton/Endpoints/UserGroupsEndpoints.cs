using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;

namespace Skeleton.Endpoints;

public record RegisterUserRequest(string Email);

public record AssignUsersToRandomUserGroupRequest(List<Guid> UserIds);

public abstract class UserGroupsEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // POST /user/register
        endpoints.MapPost("/user/register", async (RegisterUserRequest request, IDocumentSession session) =>
        {
            var userId = Guid.NewGuid();
            var @event = new UserRegisteredEvent(userId, request.Email);
            session.Events.StartStream(userId, @event); // User stream
            await session.SaveChangesAsync();
            return Results.Created($"/user/{userId}", new { userId });
        });

        // POST /user-groups/assign
        endpoints.MapPost("/user-groups/assign", async (AssignUsersToRandomUserGroupRequest request, IDocumentSession session) =>
        {
            var groupId = Guid.NewGuid();
            var @event = new UsersAssignedToGroup(groupId, request.UserIds);
            session.Events.StartStream(groupId, @event); // UserGroup stream
            await session.SaveChangesAsync();
            return Results.Created($"/user-groups/{groupId}", new { groupId });
        });

        // GET /user-groups/{groupId}
        endpoints.MapGet("/user-groups/{groupId:guid}", async (Guid groupId, IQuerySession query) =>
        {
            var result = await query.LoadAsync<UserGroupOverview>(groupId);
            return result is null
                ? Results.NotFound()
                : Results.Ok(result);
        });
    }
}

//Events
public record UserRegisteredEvent(Guid UserId, string Email);

public record UsersAssignedToGroup(Guid GroupId, List<Guid> UserIds);
// Viewmodel

public record UserGroupOverview(Guid Id, List<UserGroupOverview.UserDto> Users)
{
    public record UserDto(Guid UserId, string Email);
}

public class UserGroupOverviewProjection : MultiStreamProjection<UserGroupOverview, Guid>
{
    public UserGroupOverviewProjection()
    {
        CustomGrouping(new Slicer());
    }

    public static UserGroupOverview Create(UsersAssignedToGroup e) => new(e.GroupId, e.UserIds.Select(id => new UserGroupOverview.UserDto(id, "")).ToList());

    public static UserGroupOverview Apply(UserGroupOverview view, UserRegisteredEvent e) => view with
    {
        Users = view.Users.Select(user => user.UserId == e.UserId ? user with { Email = e.Email } : user).ToList()
    };


    public class Slicer : IEventSlicer<UserGroupOverview, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<UserGroupOverview, Guid>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupOverview, Guid>>> SliceAsyncEvents(IQuerySession querySession, List<IEvent> events)
        {
            var tenant = Tenant.ForDatabase(querySession.Database);
            var slices = new TenantSliceGroup<UserGroupOverview, Guid>(tenant);

            // 1. Slice alle UsersAssignedToGroup-events til deres respektive gruppe-id
            slices.AddEvents<UsersAssignedToGroup>(e => e.GroupId, events);

            // 2. Byg et lookup: UserId → List<GroupId>, baseret på UsersAssignedToGroup-events
            var userIdToGroupIds = events
                .OfType<IEvent<UsersAssignedToGroup>>()
                .SelectMany(e => e.Data.UserIds.Select(userId => (userId, e.Data.GroupId)))
                .ToLookup(x => x.userId, x => x.GroupId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Distinct().ToList()
                );

            // 3. Find alle UserRegisteredEvent-events
            var userRegisteredEvents = events
                .OfType<IEvent<UserRegisteredEvent>>()
                .ToList();

            // 4. For hvert UserRegisteredEvent, find de grupper brugeren er i, og tilføj eventet til hver gruppe
            foreach (var userEvent in userRegisteredEvents)
            {
                var userId = userEvent.Data.UserId;

                if (!userIdToGroupIds.TryGetValue(userId, out var groupIds)) continue; // Brugeren er ikke blevet tildelt nogen grupper i denne batch

                foreach (var groupId in groupIds)
                {
                    slices.AddEvent(groupId, userEvent);
                }
            }

            return new ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupOverview, Guid>>>(
                new List<TenantSliceGroup<UserGroupOverview, Guid>> { slices }
            );
        }
    }
}