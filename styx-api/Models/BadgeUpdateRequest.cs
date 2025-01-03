namespace Styx.Api.Models
{
    public class UpdateBadgeRequest
    {
        public string Auth0UserId { get; set; }
        public int Value { get; set; }
        public string ImageUrl { get; set; }
    }
}
