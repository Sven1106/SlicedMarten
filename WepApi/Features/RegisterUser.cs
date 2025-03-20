using System.Threading.Tasks;
using Marten;

namespace WepApi.Features;
public record RegisterUserRequest(string FirstName, string LastName, string Email);
public record RegisterUserResponse(Guid Id);
public record UserRegistered(Guid UserId, string FirstName, string LastName, string Email);
public class RegisterUserHandler(IDocumentSession session)
{
    public async Task<RegisterUserResponse> Handle(RegisterUserRequest request)
    {
        var @event = new UserRegistered(Guid.NewGuid(), request.FirstName, request.LastName, request.Email);
        session.Events.StartStream(@event.UserId, @event);
        await session.SaveChangesAsync();
        return new RegisterUserResponse(@event.UserId);
    }
}