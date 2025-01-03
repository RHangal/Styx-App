namespace Styx.Api.Models
{
    public class Badges
    {
        public string value { get; set; }
        public List<string> imageUrls { get; set; } = new List<string>();
    }
}
