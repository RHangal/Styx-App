namespace Styx.Api.Models
{
    public class StyxUser
    {
        public string id { get; set; } // Unique User ID for the database
        public string auth0UserId { get; set; } // Auth0 User ID
        public string email { get; set; } // Auth0 Email
        public string Name { get; set; } // User's Name
        public string AboutMe { get; set; } // User's About Me section
        public string Habits { get; set; } // User's habits
        public string PhotoUrl { get; set; } // User's Pfp
        public int Coins { get; set; } // User's Coin count
        public List<string> Badges { get; set; } = new List<string>(); // List of user's badges
    }
}
