using Marten.Events.Aggregation;

namespace WepApi.Features;

public record GetUserRequest(Guid Id);

public record GetUserResponse;

public class GetUserHandler
{
    public GetUserResponse Handle(GetUserRequest request)
    {
        return new GetUserResponse();
    }
}

public class GetUserProjection : SingleStreamProjection<GetUserProjection.User>
{
    public User Create(UserRegistered @event)
    {
        return new User
        {
            Id = @event.UserId,
            FirstName = @event.FirstName,
            LastName = @event.LastName,
            Email = @event.Email
        };
    }

    public void Apply(UserRenamed userRenamed, User state)
    {
        state.LastName = userRenamed.NewName;
    }

    public class User
    {
        public required Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Email { get; set; }
    }
}