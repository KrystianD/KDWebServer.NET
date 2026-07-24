namespace KDWebServer.Auth;

public class NotAuthenticatedAuthState : IAuthState
{
  public static readonly NotAuthenticatedAuthState Instance = new();

  public bool IsAuthenticated => false;

  private NotAuthenticatedAuthState() { }
}