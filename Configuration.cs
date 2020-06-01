using System;

namespace ZoomSlackStatus
{
    public static class Configuration
    {
        public static readonly string BaseUri = Environment.GetEnvironmentVariable("BaseUri");
        public static readonly string ZoomAuthorizationSuccessUri = System.Web.HttpUtility.UrlEncode($"{BaseUri}/api/ZoomAuthorizationSuccess");
        public static readonly string SlackAuthorizationSuccessUri = System.Web.HttpUtility.UrlEncode($"{BaseUri}/api/SlackAuthorizationSuccess");
        public static readonly string ZoomClientId = Environment.GetEnvironmentVariable("ZoomClientId");
        public static readonly string ZoomClientSecret = Environment.GetEnvironmentVariable("ZoomClientSecret");
        public static readonly string ZoomVerificationToken = Environment.GetEnvironmentVariable("ZoomVerificationToken");
        public static readonly string SlackClientId = Environment.GetEnvironmentVariable("SlackClientId");
        public static readonly string SlackClientSecret = Environment.GetEnvironmentVariable("SlackClientSecret");
        public static readonly string CloudStorageAccountConnectionString = Environment.GetEnvironmentVariable("CloudStorageAccountConnectionString");
        public static readonly string InAMeetingStatusEmoji = Environment.GetEnvironmentVariable("InAMeetingStatusEmoji");
        public static readonly string InAMeetingStatusText = Environment.GetEnvironmentVariable("InAMeetingStatusText");
    }
}