namespace WepApi.Features;

public record UserRenamed(Guid UserId, string NewName);

public class RenameUser
{
}