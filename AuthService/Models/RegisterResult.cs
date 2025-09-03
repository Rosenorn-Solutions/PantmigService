namespace AuthService.Models
{
    public class RegisterResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public AuthResponse? AuthResponse { get; set; }
    }
}
