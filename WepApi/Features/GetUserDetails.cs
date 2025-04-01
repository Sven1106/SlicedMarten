namespace WepApi.Features;
public record GetUserDetailsRequest(Guid Id);
public record GetUserDetailsResponse();
public class GetUserDetailsHandler
{
  public GetUserDetailsResponse Handle(GetUserDetailsRequest request)
  {
    return new GetUserDetailsResponse();
  }
}


public record UserDetails(Guid Id, string FirstName, string LastName, string Email)
{
  public static UserDetails Create(UserRegistered @event) => new(@event.UserId, @event.FirstName, @event.LastName, @event.Email);

  public static UserDetails Apply(UserRenamed userRenamed, UserDetails state) => state with
  {
    LastName = userRenamed.NewName
  };
}
