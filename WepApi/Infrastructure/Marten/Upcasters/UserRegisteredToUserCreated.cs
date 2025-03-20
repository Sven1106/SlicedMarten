using Marten.Services.Json.Transformations;
using WepApi.Features;
namespace WepApi.Infrastructure.Marten.Upcasters;
public record UserCreated(Guid Id, string Name, string Email);
public class UserCreatedToUserRegistered : EventUpcaster<UserCreated, UserRegistered>
{
    protected override UserRegistered Upcast(UserCreated oldEvent)
    {
        return new UserRegistered(oldEvent.Id,
            oldEvent.Name,
            "UNKNOWN",
            oldEvent.Email
        );
    }

}