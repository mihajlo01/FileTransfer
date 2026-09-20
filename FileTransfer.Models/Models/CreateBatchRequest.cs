namespace FileTransfer.Server.Models
{
    public class CreateBatchRequest
    {
        public List<CreateFileRequest> Files { get; set; }
    }
}
