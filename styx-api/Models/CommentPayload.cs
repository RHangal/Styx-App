namespace Styx.Api.Models
{
    public class CommentPayload
    {
        public string PostId { get; set; } // ID of the post to comment on
        public string auth0UserId { get; set; } // ID of the user who is commenting
        public string Name { get; set; } // ID of the user who is commenting
        public string Text { get; set; } // The comment text
        public string Email { get; set; } // The comment text
        public string CommentId { get; set; } // The comment id (optional)
    }
}
