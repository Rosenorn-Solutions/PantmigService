namespace PantmigService.Entities
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int ListingId { get; set; }
        public string ChatSessionId { get; set; } = string.Empty;
        public string SenderUserId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }
}
