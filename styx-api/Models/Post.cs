namespace Styx.Api.Models
{
    public class Post
    {
        public string id { get; set; }
        public string PostType { get; set; }
        public string auth0UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Caption { get; set; }
        public string MediaUrl { get; set; }
        public List<string> Likes { get; set; } = new List<string>();
        public int LikesCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<Comment> Comments { get; set; } = new List<Comment>(); // List of comments for the post
    }
}
