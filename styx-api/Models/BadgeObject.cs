namespace Styx.Api.Models
{
    public class BadgeObject
    {
        public string id { get; set; }
        public List<Badges> badges { get; set; } = new List<Badges>();
    }
}
