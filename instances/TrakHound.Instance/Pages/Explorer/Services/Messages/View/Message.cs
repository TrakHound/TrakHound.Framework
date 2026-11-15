namespace TrakHound.Instance.Pages.Explorer.Services.Messages.View
{
    public class Message
    {
        public string Topic { get; set; }

        public Stream Content { get; set; }

        public bool Retain { get; set; }

        public byte Qos { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
