namespace FileTransfer.Server.Models
{
    public class BatchStatusResponse
    {
        public Guid BatchId { get; set; }
        public string Status { get; set; } = "Unknown";
        public int TotalFiles { get; set; }
    }
}
