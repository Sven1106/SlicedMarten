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
        // POST /user-groups/register
        endpoints.MapPost("/user/register", async (RegisterUserRequest request, IDocumentSession session) =>
        {
            var userId = Guid.NewGuid();
            var @event = new UserRegisteredEvent(userId, request.Email);
            session.Events.StartStream<UserGroupsAssignmentViewModel>(userId, @event);
            await session.SaveChangesAsync();
            return Results.Created($"/user/{userId}", new { userId });
        });

        // POST /user-groups/assign
        endpoints.MapPost("/user-groups/assign", async (AssignUsersToRandomUserGroupRequest request, IDocumentSession session) =>
        {
            var groupId = Guid.NewGuid();
            var @event = new MultipleUsersAssignedToGroupEvent(groupId, request.UserIds);
            foreach (var userId in request.UserIds)
            {
                session.Events.Append(userId, @event);
            }

            await session.SaveChangesAsync();
            return Results.Accepted();
        });

        // GET /user-groups/{userId}
        endpoints.MapGet("/user-groups/{userId:guid}", async (Guid userId, IQuerySession query) =>
        {
            var result = await query.LoadAsync<UserGroupsAssignmentViewModel>(userId);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new UserGroupsAssignmentViewModel(result.UserId, result.GroupIds));
        });
    }
}

//Events
public record UserRegisteredEvent(Guid UserId, string Email);

public record MultipleUsersAssignedToGroupEvent(Guid GroupId, List<Guid> UserIds);

// Viewmodel

public record UserGroupsAssignmentViewModel(Guid UserId, List<Guid> GroupIds);

public class UserGroupsAssignmentProjection : MultiStreamProjection<UserGroupsAssignmentViewModel, Guid>
{
    public class CustomSlicer : IEventSlicer<UserGroupsAssignmentViewModel, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<UserGroupsAssignmentViewModel, Guid>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupsAssignmentViewModel, Guid>>> SliceAsyncEvents(IQuerySession querySession, List<IEvent> events)
        {
            var group = new TenantSliceGroup<UserGroupsAssignmentViewModel, Guid>(Tenant.ForDatabase(querySession.Database));
            group.AddEvents<UserRegisteredEvent>(@event => @event.UserId, events);
            group.AddEvents<MultipleUsersAssignedToGroupEvent>(@event => @event.UserIds, events);

            return new ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupsAssignmentViewModel, Guid>>>(new List<TenantSliceGroup<UserGroupsAssignmentViewModel, Guid>> { group });
        }
    }

    public UserGroupsAssignmentProjection()
    {
        CustomGrouping(new CustomSlicer());
    }

    public static UserGroupsAssignmentViewModel Create(UserRegisteredEvent e) => new(e.UserId, []);

    public static UserGroupsAssignmentViewModel Apply(UserGroupsAssignmentViewModel view, MultipleUsersAssignedToGroupEvent e) => view with
    {
        GroupIds = view.GroupIds.Contains(e.GroupId) ? view.GroupIds : view.GroupIds.Append(e.GroupId).ToList()
    };
}

public record UserGroupView(Guid GroupId, List<Guid> UserIds);

public class UserGroupProjection : MultiStreamProjection<UserGroupView, Guid>
{
    public class CustomSlicer : IEventSlicer<UserGroupView, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<UserGroupView, Guid>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupView, Guid>>> SliceAsyncEvents(IQuerySession querySession, List<IEvent> events)
        {
            var group = new TenantSliceGroup<UserGroupView, Guid>(Tenant.ForDatabase(querySession.Database));
            group.AddEvents<MultipleUsersAssignedToGroupEvent>(e => e.GroupId, events);

            return new(new List<TenantSliceGroup<UserGroupView, Guid>> { group });
        }
    }

    public UserGroupProjection()
    {
        CustomGrouping(new CustomSlicer());
    }

    public static UserGroupView Create(MultipleUsersAssignedToGroupEvent e) => new(e.GroupId, e.UserIds);

    public static UserGroupView Apply(UserGroupView view, MultipleUsersAssignedToGroupEvent e)
    {
        var merged = view.UserIds.Union(e.UserIds).Distinct().ToList();
        return view with { UserIds = merged };
    }
}