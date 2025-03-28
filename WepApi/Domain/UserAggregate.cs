using Marten.Events.Aggregation;
using WepApi.Features;

namespace WepApi.Domain;

public class UserAggregate : SingleStreamProjection<UserAggregate.User>
{
    public class User
    {
        public required Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Email { get; set; }
    }

    public User Create(UserRegistered @event) => new()
    {
        Id = @event.UserId,
        FirstName = @event.FirstName,
        LastName = @event.LastName,
        Email = @event.Email
    };

    public void Apply(UserRenamed userRenamed, User user)
    {
        user.LastName = userRenamed.NewName;
    }
}