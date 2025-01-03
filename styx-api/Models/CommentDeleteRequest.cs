namespace Styx.Api.Models
{
    public class CommentDeleteRequest
    {
        public string PostId { get; set; }
        public string CommentId { get; set; }
        public string ReplyId { get; set; } // Optional, can be null or empty
        public string Auth0UserId { get; set; }
    }
}
