using Marten.Events.Aggregation;

namespace WepApi.Features.GetUser;

public record Request();
public record Response();
public class Handler { 
    public Response Handle(Request request){
		return new Response();
    }
}


public class GetUserProjection : SingleStreamProjection<User>
{
}

public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
}