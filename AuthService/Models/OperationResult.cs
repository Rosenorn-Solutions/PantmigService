namespace AuthService.Models
{
    public class OperationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
    public class ChangeEmailResult : OperationResult
    {
        public bool RequiresConfirmation { get; set; }
    }
    public class ChangePasswordResult : OperationResult
    {
        public AuthResponse? AuthResponse { get; set; }
    }
}
