namespace AuthService.Models
{
    public class AuthResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiration { get; set; }
        public string? LinkedInId { get; set; }
        public string? FindJobNuUri { get; set; }
        // New: echo back assigned role
        public UserType UserType { get; set; }

        // Optional city info for convenience
        public int? CityId { get; set; }
        public string? CityName { get; set; }
    }
}
