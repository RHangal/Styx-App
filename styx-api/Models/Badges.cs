namespace Styx.Api.Models
{
    public class Badges
    {
        public int value { get; set; }
        public List<string> imageUrls { get; set; } = new List<string>();
    }
}
