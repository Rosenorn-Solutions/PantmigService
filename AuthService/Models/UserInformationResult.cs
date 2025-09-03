namespace AuthService.Models
{
    public class UserInformationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public UserInformationDTO? UserInformation { get; set; }
    }
}
