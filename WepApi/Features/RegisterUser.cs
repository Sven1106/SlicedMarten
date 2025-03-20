using Marten.Services.Json.Transformations;

namespace WepApi.Features;

public class RegisterUser
{
    public static class V1
    {
        public record UserRegistered(Guid Id, string Name, string Email);
    }

    public static class V2
    {
        public record UserCreated(Guid Id, string FirstName, string LastName, string Email);

        public class UserCreatedUpcaster : EventUpcaster<V1.UserRegistered, UserCreated>
        {
            protected override UserCreated Upcast(V1.UserRegistered oldEvent)
            {
                return new UserCreated(oldEvent.Id,
                    oldEvent.Name,
                    "UNKNOWN",
                    oldEvent.Email
                );
            }
        }
    }
}