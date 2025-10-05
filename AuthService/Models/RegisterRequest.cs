namespace AuthService.Models
{
    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; }  = string.Empty;
        public string MitId { get; set; } = string.Empty;
        // New: role selection at registration
        public UserType UserType { get; set; } = UserType.Recycler;

        // New demographic fields (optional)
        public Gender Gender { get; set; } = Gender.Unknown;
        public DateOnly? BirthDate { get; set; }

        // Optional: City (Bopæls by)
        public string? City { get; set; }
    }
}
