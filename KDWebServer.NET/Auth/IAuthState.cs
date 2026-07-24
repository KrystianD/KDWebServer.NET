namespace KDWebServer.Auth;

public interface IAuthState
{
  bool IsAuthenticated { get; }
}