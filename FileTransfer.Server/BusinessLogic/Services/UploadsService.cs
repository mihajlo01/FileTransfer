using FileTransfer.Server.BusinessLogic.Interfaces;
using FileTransfer.Server.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace FileTransfer.Server.BusinessLogic.Services
{
    public class UploadsService : IUploadsService
    {
        private static readonly ConcurrentDictionary<Guid, ConcurrentBag<int>> _receivedChunks = new();
        private static readonly ConcurrentDictionary<Guid, BatchStatusResponse> _batchStore = new();

        private class CachedFileMetadata
        {
            public Guid FieldId { get; set; }
            public string FileName { get; set; } = string.Empty;
            public int ChunkCount { get; set; }
            public string DestinationFolder { get; set; } = string.Empty;
        }

        private static readonly ConcurrentDictionary<Guid, List<CachedFileMetadata>> _batchFilesMetadata = new();

        public async Task<bool> SaveChunkAsync(Guid fileId, int chunkIndex, Stream body, byte[] expectedMd5)
        {
            var partPath = GetPartPath(fileId, chunkIndex);
            var tmp = $"{partPath}.tmp";

            Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);

            byte[] actual;
            using (var md5 = MD5.Create())
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await body.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    md5.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
                }
                md5.TransformFinalBlock(buffer, 0, 0);
                actual = md5.Hash!;
            }

            if (!actual.SequenceEqual(expectedMd5))
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Server Error] MD5 mismatch detected for chunk {chunkIndex}. Requesting re-submission.");
                Console.ResetColor();
                return false;
            }

            if (File.Exists(partPath)) File.Delete(partPath);
            File.Move(tmp, partPath);

            MarkReceived(fileId, chunkIndex);
            return true;
        }

        public CreateBatchResponse CreateBatch(CreateBatchRequest request, string userName)
        {
            var batchId = Guid.NewGuid();
            var filesToUpload = new List<FileUploadInfo>();
            var cachedMetadata = new List<CachedFileMetadata>();

            foreach (var fileRequest in request.Files)
            {
                int chunkCount = (int)Math.Ceiling((double)fileRequest.SizeBytes / fileRequest.ChunkSizeBytes);
                if (chunkCount == 0) chunkCount = 1;

                var fieldId = Guid.NewGuid();
                filesToUpload.Add(new FileUploadInfo
                {
                    FieldId = fieldId,
                    FileName = fileRequest.FileName,
                    ChunkCount = chunkCount,
                    MaxParallelChunks = 4
                });

                cachedMetadata.Add(new CachedFileMetadata
                {
                    FieldId = fieldId,
                    FileName = fileRequest.FileName,
                    ChunkCount = chunkCount,
                    DestinationFolder = request.DestinationFolder
                });
            }

            _batchFilesMetadata[batchId] = cachedMetadata;
            _batchStore[batchId] = new BatchStatusResponse { BatchId = batchId, Status = "Initialized", TotalFiles = filesToUpload.Count };

            return new CreateBatchResponse { BatchId = batchId, Files = filesToUpload };
        }

        public BatchStatusResponse GetStatus(Guid batchId) =>
            _batchStore.TryGetValue(batchId, out var status) ? status : new BatchStatusResponse { BatchId = batchId, Status = "NotFound" };

        public async Task<CompleteBatchResult> CompleteAsync(Guid batchId)
        {
            if (!_batchStore.TryGetValue(batchId, out var status) || !_batchFilesMetadata.TryGetValue(batchId, out var filesMetadata))
            {
                return new CompleteBatchResult { Success = false, Error = "Batch session identifier or metadata not found." };
            }

            try
            {
                foreach (var fileInfo in filesMetadata)
                {
                    var targetFolder = string.IsNullOrWhiteSpace(fileInfo.DestinationFolder)
                        ? Path.Combine(Directory.GetCurrentDirectory(), "CompletedUploads")
                        : fileInfo.DestinationFolder;

                    Directory.CreateDirectory(targetFolder);
                    var finalFilePath = Path.Combine(targetFolder, fileInfo.FileName);

                    if (File.Exists(finalFilePath)) File.Delete(finalFilePath);

                    using (var finalFileStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                    {
                        for (int chunkIndex = 0; chunkIndex < fileInfo.ChunkCount; chunkIndex++)
                        {
                            var partPath = GetPartPath(fileInfo.FieldId, chunkIndex);
                            using (var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                await partStream.CopyToAsync(finalFileStream);
                            }
                        }
                    }

                    string finalSha256;
                    using (var shaEvaluator = SHA256.Create())
                    using (var assembledReader = new FileStream(finalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                    {
                        var rawBytes = await shaEvaluator.ComputeHashAsync(assembledReader);
                        finalSha256 = BitConverter.ToString(rawBytes).Replace("-", "").ToLowerInvariant();
                    }

                    var chunkDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileInfo.FieldId.ToString());
                    if (Directory.Exists(chunkDirectoryPath)) Directory.Delete(chunkDirectoryPath, true);
                    _receivedChunks.TryRemove(fileInfo.FieldId, out _);

                    return new CompleteBatchResult { Success = true, Error = finalSha256 };
                }

                _batchFilesMetadata.TryRemove(batchId, out _);
                return new CompleteBatchResult { Success = true };
            }
            catch (Exception ex)
            {
                return new CompleteBatchResult { Success = false, Error = $"Assembly processing validation fault crashed: {ex.Message}" };
            }
        }

        private string GetPartPath(Guid fileId, int chunkIndex) =>
            Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileId.ToString(), $"chunk_{chunkIndex}.part");

        private void MarkReceived(Guid fileId, int chunkIndex)
        {
            var chunks = _receivedChunks.GetOrAdd(fileId, _ => new ConcurrentBag<int>());
            if (!chunks.Contains(chunkIndex)) chunks.Add(chunkIndex);
        }
    }
}
