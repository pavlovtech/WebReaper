namespace WebReaper.Proxy.Concrete
{
    public class Proxy
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Proxy_Address { get; set; }
        public Dictionary<string, int> Ports { get; set; }
    }
}
