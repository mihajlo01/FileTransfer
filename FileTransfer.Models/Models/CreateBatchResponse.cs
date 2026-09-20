namespace FileTransfer.Server.Models
{
    public class CreateBatchResponse
    {
        public Guid BatchId { get; set; }
        public List<FileUploadInfo> Files { get; set; } = new();
    }
}
