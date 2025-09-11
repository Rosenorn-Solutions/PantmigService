namespace AuthService.Entities
{
    public class CityPostalCode
    {
        public int Id { get; set; }
        public int CityId { get; set; }
        public string PostalCode { get; set; } = string.Empty;

        public City? City { get; set; }
    }
}
