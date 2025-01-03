namespace Styx.Api.Models
{
    public class Comment
    {
        public string CommentId { get; set; } = Guid.NewGuid().ToString(); // Unique ID for each comment
        public string auth0UserId { get; set; } // ID of the user who made the comment
        public string Name { get; set; } // ID of the user who made the comment
        public string Text { get; set; }
        public string Email { get; set; }
        public List<string> Likes { get; set; } = new List<string>();
        public int LikesCount { get; set; }
        public List<Comment> Replies { get; set; } = new List<Comment>(); // For nesting replies
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
