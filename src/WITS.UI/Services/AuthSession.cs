namespace WITS.UI.Services;

/// <summary>
/// Holds the signed-in user's access token for the current Blazor circuit.
/// shortcut: in-memory only, a browser refresh requires signing in again.
/// </summary>
public class AuthSession
{
    public string? Token { get; private set; }
    public string? Username { get; private set; }
    public bool IsAuthenticated => Token is not null;

    public void SignIn(string token, string username)
    {
        Token = token;
        Username = username;
    }

    public void SignOut()
    {
        Token = null;
        Username = null;
    }
}
