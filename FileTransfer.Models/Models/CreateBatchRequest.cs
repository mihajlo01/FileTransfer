namespace FileTransfer.Server.Models
{
    public class CreateBatchRequest
    {
        public string DestinationFolder { get; set; } = string.Empty;
        public List<CreateFileRequest> Files { get; set; } = new();
    }
}
