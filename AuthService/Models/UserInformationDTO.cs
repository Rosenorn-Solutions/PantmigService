namespace AuthService.Models
{
    public class UserInformationDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsEmailConfirmed { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsOrganization { get; set; } = false;

        public int? CityId { get; set; }
        public string? CityName { get; set; }

        public decimal Rating { get; set; }

        // New demographic fields
        public Gender Gender { get; set; }
        public DateOnly? BirthDate { get; set; }
    }
}
