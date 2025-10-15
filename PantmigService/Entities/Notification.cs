namespace PantmigService.Entities
{
    public enum NotificationType
    {
        RecyclerApplied = 1,
        DonorAccepted = 2,
        ChatMessage = 3,
        MeetingSet = 4
    }

    public class Notification
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty; // recipient
        public int ListingId { get; set; }
        public NotificationType Type { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }
}
