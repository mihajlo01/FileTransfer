namespace FileTransfer.Server.Models
{
    internal class CachedFileMetadata
    {
        public Guid FieldId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int ChunkCount { get; set; }
        public string ExpectedMd5Hex { get; set; } = string.Empty;
        public string DestinationFolder { get; set; } = string.Empty;
    }
}
