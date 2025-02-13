using System.Text.Json.Serialization;

namespace haworks.Models
{
    public class DatabaseCredentials
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }
}
