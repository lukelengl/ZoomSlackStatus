using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using Microsoft.Azure.Cosmos.Table;

namespace ZoomSlackStatus
{
    public class Functions
    {
        private readonly HttpClient _httpClient;

        public Functions(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// Redirect user to Zoom authorization
        [FunctionName("Install")]
        public Task<IActionResult> Install(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogDebug("Install function started.");
            return Task.FromResult<IActionResult>(new RedirectResult($"https://zoom.us/oauth/authorize?response_type=code&client_id={Configuration.ZoomClientId}&redirect_uri={Configuration.ZoomAuthorizationSuccessUri}"));
        }

        /// After Zoom authorization is successful, it will redirect here, which will redirect to Slack authorization 
        [FunctionName("ZoomAuthorizationSuccess")]
        public async Task<IActionResult> ZoomAuthorizationSuccess(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogDebug("Install2 function started.");

            if (!req.Query.ContainsKey("code"))
            {
                return new BadRequestObjectResult("Missing 'code' parameter.");
            }

            var code = req.Query["code"];

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"https://zoom.us/oauth/token?grant_type=authorization_code&code={code}&redirect_uri={Configuration.ZoomAuthorizationSuccessUri}");
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes($"{Configuration.ZoomClientId}:{Configuration.ZoomClientSecret}")));
            var tokenResponse = await _httpClient.SendAsync(tokenRequest);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                log.LogCritical($"Error acquiring Zoom tokens: {tokenResponse}");
                return InternalServerError();
            }

            dynamic tokenResponseContent = JsonConvert.DeserializeObject(await tokenResponse.Content.ReadAsStringAsync());
            string accessToken = tokenResponseContent.access_token;

            var getUserRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.zoom.us/v2/users/me");
            getUserRequest.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
            var getUserInfoResponse = await _httpClient.SendAsync(getUserRequest);
            if (!getUserInfoResponse.IsSuccessStatusCode)
            {
                log.LogCritical($"Error acquiring Zoom user info: {getUserInfoResponse}");
                return InternalServerError();
            }

            dynamic getUserInfoResponseContent = JsonConvert.DeserializeObject(await getUserInfoResponse.Content.ReadAsStringAsync());
            string userId = getUserInfoResponseContent.id;

            return new RedirectResult($"https://slack.com/oauth/v2/authorize?user_scope=users.profile:read%20users.profile:write&client_id={Configuration.SlackClientId}&redirect_uri={Configuration.SlackAuthorizationSuccessUri}&state={userId.ToLower()}");
        }

        // After Slack authorization is successful, save the user's email and access token in Azure Table Storage.
        [FunctionName("SlackAuthorizationSuccess")]
        public async Task<IActionResult> SlackAuthorizationSuccess(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogDebug("Install3 function started.");
            if (!req.Query.ContainsKey("code")) 
            {
                return new BadRequestObjectResult("Missing 'code' parameter.");
            }
            if (!req.Query.ContainsKey("state")) 
            {
                return new BadRequestObjectResult("Missing 'state' parameter.");
            }
            
            var code = req.Query["code"];
            var state = req.Query["state"];

            var accessTokenResponse = await _httpClient.PostAsync($"https://slack.com/api/oauth.v2.access?client_id={Configuration.SlackClientId}&client_secret={Configuration.SlackClientSecret}&redirect_uri={Configuration.SlackAuthorizationSuccessUri}&code={code}", null);
            if (!accessTokenResponse.IsSuccessStatusCode)
            {
                log.LogCritical($"Error acquiring Slack access token: {accessTokenResponse}");
                return InternalServerError();
            }
            dynamic accessTokenResponseContent = JsonConvert.DeserializeObject(await accessTokenResponse.Content.ReadAsStringAsync());
            string accessToken = accessTokenResponseContent.authed_user.access_token;

            var getUserResponse = await _httpClient.GetAsync($"https://slack.com/api/users.profile.get?token={accessToken}");
            if (!getUserResponse.IsSuccessStatusCode)
            {
                log.LogCritical($"Error getting user profile: {getUserResponse}");
                return InternalServerError();
            }
            dynamic getUserResponseContent = JsonConvert.DeserializeObject(await getUserResponse.Content.ReadAsStringAsync());
            string email = getUserResponseContent.profile.email;
            await SaveUserAsync(new User(state, accessToken, email));

            log.LogInformation($"User {email} has successfully installed the integration.");
            return new OkObjectResult($"You have successfully installed this integration.");
        }

        // handle event from Zoom webhook
        [FunctionName("ZoomWebhook")]
        public async Task<IActionResult> ZoomWebhook(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            if (!req.Headers.ContainsKey("authorization") || req.Headers["authorization"] != Configuration.ZoomVerificationToken)
            {
                log.LogError("Verification token was missing.");
                return new ForbidResult();
            }

            dynamic body = JsonConvert.DeserializeObject(await req.ReadAsStringAsync());
            log.LogInformation($"Event body: {body}");
            string @event = body.@event;
            if (@event != "user.presence_status_updated")
            {
                log.LogCritical($"Unexpected Zoom webhook event encountered: {@event}.");
                return InternalServerError();
            }

            string id = body.payload.@object.id;
            string presenceStatus = body.payload.@object.presence_status;

            var user = await GetUserAsync(id.ToLower());
            if (user == null) {
                log.LogCritical($"User {id} not found");
                return InternalServerError();
            }

            var getUserResponse = await _httpClient.GetAsync($"https://slack.com/api/users.profile.get?token={user.SlackAccessToken}");
            if (!getUserResponse.IsSuccessStatusCode)
            {
                log.LogCritical($"Error getting user profile: {getUserResponse}");
                return InternalServerError();
            }

            log.LogInformation($"Current user status: {getUserResponse}");

            dynamic getUserResponseContent = JsonConvert.DeserializeObject(await getUserResponse.Content.ReadAsStringAsync());
            string currentStatusEmoji = getUserResponseContent.profile.status_emoji;
            string currentStatusText = getUserResponseContent.profile.status_text;

            if ((currentStatusEmoji == Configuration.InAMeetingStatusEmoji && currentStatusText == Configuration.InAMeetingStatusText)
                || (string.IsNullOrEmpty(currentStatusEmoji) && string.IsNullOrEmpty(currentStatusText)))
            {
                var setUserProfile = new SetUserProfile
                {
                    Profile = new Profile
                    {
                        StatusEmoji = IsBusy(presenceStatus) ? Configuration.InAMeetingStatusEmoji : string.Empty,
                        StatusText = IsBusy(presenceStatus) ? Configuration.InAMeetingStatusText : string.Empty
                    }
                };

                var setUserRequest = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/users.profile.set");
                setUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.SlackAccessToken);
                setUserRequest.Content = new StringContent(JsonConvert.SerializeObject(setUserProfile), System.Text.Encoding.UTF8, "application/json");
                var setUserResponse = await _httpClient.SendAsync(setUserRequest);
                if (!setUserResponse.IsSuccessStatusCode) 
                {
                    return InternalServerError();
                }
                var setUserResponseContent = await setUserResponse.Content.ReadAsStringAsync();
                log.LogDebug($"Response from setting user profile: {setUserResponseContent}");
            }
            
            return new OkResult();
        }

        private bool IsBusy(string presenceStatus)
        {
            switch(presenceStatus)
            {
                case "Do_Not_Disturb":
                case "In_Meeting":
                    return true;
            }
            return false;
        }

        private StatusCodeResult InternalServerError()
        {
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        private async Task SaveUserAsync(User user) 
        {
            var userTable = await GetUserTableAsync();
            var insertUserOperation = TableOperation.InsertOrMerge(user);
            var insertUserResult = await userTable.ExecuteAsync(insertUserOperation);
        }

        private async Task<User> GetUserAsync(string zoomUserId)
        {
            var userTable = await GetUserTableAsync();
            var getUserOperation = TableOperation.Retrieve<User>(zoomUserId, zoomUserId);
            var getResult = await userTable.ExecuteAsync(getUserOperation);
            return getResult.Result as User;
        }

        private async Task<CloudTable> GetUserTableAsync() 
        {
            var storageAccount = CloudStorageAccount.Parse(Configuration.CloudStorageAccountConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());
            var cloudTable = tableClient.GetTableReference("Users");
            await cloudTable.CreateIfNotExistsAsync();
            return cloudTable;
        }
    }
}
