using Marten.Events.Aggregation;
using Marten.Services.Json.Transformations;

namespace WepApi.Features;

public class GetUsers
{
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