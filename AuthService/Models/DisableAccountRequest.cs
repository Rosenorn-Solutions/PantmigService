namespace AuthService.Models
{
    public class DisableAccountRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }
}
