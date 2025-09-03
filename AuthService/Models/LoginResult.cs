namespace AuthService.Models
{
    public class LoginResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public AuthResponse? AuthResponse { get; set; }
    }
}
