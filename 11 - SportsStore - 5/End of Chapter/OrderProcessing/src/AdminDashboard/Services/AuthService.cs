namespace AdminDashboard.Services;

/// <summary>
/// Scoped per Blazor circuit — tracks whether the current admin session is authenticated.
/// </summary>
public class AuthService
{
    private const string ValidUsername = "admin";
    private const string ValidPassword = "Admin@Sports2026!";

    public bool IsAuthenticated { get; private set; }

    public bool Login(string username, string password)
    {
        if (username == ValidUsername && password == ValidPassword)
        {
            IsAuthenticated = true;
            return true;
        }
        return false;
    }

    public void Logout() => IsAuthenticated = false;
}
