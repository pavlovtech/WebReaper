namespace WebReaper.ProxyProviders.WebShareProxy
{
    // WebShare API response shape; all properties are populated by the
    // System.Text.Json deserializer.
    public class Proxy
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Proxy_Address { get; set; } = "";
        public Dictionary<string, int> Ports { get; set; } = new();
    }
}
