using WepApi.Features;

namespace WepApi.Domain;

public record User(Guid Id, string FirstName, string LastName, string Email)
{
    public static User Create(UserRegistered @event)
    {
        return new User(@event.UserId, @event.FirstName, @event.LastName, @event.Email);
    }

    public static User Apply(UserRenamed userRenamed, User state)
    {
        return state with
        {
            LastName = userRenamed.NewName
        };
    }
}