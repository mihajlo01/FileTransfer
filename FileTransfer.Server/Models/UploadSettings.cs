namespace FileTransfer.Server.Models
{
    public class UploadSettings
    {
        public int DefaultChunkSize { get; set; } = 8 * 1024 * 1024;
        public int MaxParallelChunks { get; set; } = 4;
        public int MaxRetries { get; set; } = 3;
        public string TemporaryUploadsFolder { get; set; } = "Uploads";
        public string DefaultCompletedFolder { get; set; } = "CompletedUploads";
        public bool EnableMasterShaValidation { get; set; } = true;
    }
}
