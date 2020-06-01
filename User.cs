using Microsoft.Azure.Cosmos.Table;

namespace ZoomSlackStatus
{
    public class User : TableEntity
	{
		public User()
		{
		}

		public User(string zoomUserId, string slackAccessToken, string email) : base(zoomUserId, zoomUserId)
		{
            ZoomUserId = zoomUserId;
            SlackAccessToken = slackAccessToken;
            Email = email;
		}

        public string ZoomUserId { get; set;}
        public string SlackAccessToken { get; set;}
        public string Email { get; set;}
	}
}