using Newtonsoft.Json;

namespace ZoomSlackStatus 
{
    public class SetUserProfile
    {
        [JsonProperty("profile")]
        public Profile Profile { get; set; }
    }

    public class Profile
    {
        [JsonProperty("status_emoji")]
        public string StatusEmoji { get; set; }
        [JsonProperty("status_text")]
        public string StatusText { get; set; }
    }
}