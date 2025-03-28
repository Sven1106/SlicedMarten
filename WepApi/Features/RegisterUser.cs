namespace WepApi.Features;
public record UserRegistered(Guid UserId, string FirstName, string LastName, string Email);
public class RegisterUser
{
}