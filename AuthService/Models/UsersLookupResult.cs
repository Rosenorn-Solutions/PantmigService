namespace AuthService.Models
{
    public class UsersLookupResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<UserRatingDTO> Users { get; set; } = new();
    }
}
