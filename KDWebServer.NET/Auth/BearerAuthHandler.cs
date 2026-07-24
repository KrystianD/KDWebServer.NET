using System.Threading.Tasks;

namespace KDWebServer.Auth;

public delegate ValueTask<IAuthState> BearerAuthHandler(string bearerToken);