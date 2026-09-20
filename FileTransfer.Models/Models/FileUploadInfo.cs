namespace FileTransfer.Server.Models
{
    public class FileUploadInfo
    {
        public Guid FieldId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int ChunkCount { get; set; }
        public int MaxParallelChunks { get; set; }
    }
}
