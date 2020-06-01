using Microsoft.Azure.Cosmos.Table;

namespace ZoomSlackStatus
{
    public class User : TableEntity
	{
		public User()
		{
		}

		public User(string zoomUserAccountId, string slackAccessToken, string email) : base(zoomUserAccountId, zoomUserAccountId)
		{
            ZoomUserAccountId = zoomUserAccountId;
            SlackAccessToken = slackAccessToken;
            Email = email;
		}

        public string ZoomUserAccountId { get; set;}
        public string SlackAccessToken { get; set;}
        public string Email { get; set;}
	}
}