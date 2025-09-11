namespace AuthService.Entities
{
    public class City
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty; // normalized key, unique
    }
}
