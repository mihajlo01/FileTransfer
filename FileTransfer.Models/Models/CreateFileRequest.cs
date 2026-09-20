namespace FileTransfer.Server.Models
{
    public class CreateFileRequest
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Md5Hex { get; set; } = string.Empty;
        public int ChunkSizeBytes { get; set; }
    }
}
