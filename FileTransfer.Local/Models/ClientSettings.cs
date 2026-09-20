namespace FileTransfer.Local.Client.Models
{
    public class ClientSettings
    {
        public string ServerBaseUrl { get; set; } = "http://localhost:5092";
        public string SourceFolder { get; set; } = string.Empty;
        public string DestinationFolder { get; set; } = string.Empty;
        public int ChunkSizeMB { get; set; } = 8;
        public int MaxConcurrency { get; set; } = 4;
        public int MaxRetries { get; set; } = 3;
    }
}
