namespace AuthService.Models
{
    public class ChangeEmailRequest
    {
        public string NewEmail { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
    }
}
