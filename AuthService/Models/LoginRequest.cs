namespace AuthService.Models
{
    public class LoginRequest
    {
        // Accept either email or username
        public string EmailOrUsername { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
