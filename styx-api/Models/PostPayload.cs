namespace Styx.Api.Models
{
    public class PostPayload
    {
        public string PostType { get; set; }
        public string Auth0UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Caption { get; set; }
        public string MediaUrl { get; set; } // Optional field for media URL
    }
}
