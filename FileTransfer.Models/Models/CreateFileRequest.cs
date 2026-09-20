namespace FileTransfer.Server.Models
{
    public class CreateFileRequest
    {
        public string FileName { get; set; }
        public long SizeBytes { get; set; }
        public string Md5Hex { get; set; }
        public int ChunkSizeBytes { get; set; }
    }
}
