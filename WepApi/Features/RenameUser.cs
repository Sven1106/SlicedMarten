namespace WepApi.Features;

public class RenameUser
{
    public static class V1
    {
        public record UserRenamed(string UserId, string NewName);
    }
}