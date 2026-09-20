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
            public string ExpectedMd5Hex { get; set; } = string.Empty;
        }

        private static readonly ConcurrentDictionary<Guid, List<CachedFileMetadata>> _batchFilesMetadata = new();

        public async Task<bool> SaveChunkAsync(Guid fileId, int chunkIndex, Stream body, byte[] expectedMd5)
        {
            var partPath = GetPartPath(fileId, chunkIndex);
            var tmp = $"{partPath}.tmp";

            var directory = Path.GetDirectoryName(partPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
                    ExpectedMd5Hex = fileRequest.Md5Hex?.ToLowerInvariant().Trim() ?? string.Empty
                });
            }

            var response = new CreateBatchResponse
            {
                BatchId = batchId,
                Files = filesToUpload
            };

            _batchFilesMetadata[batchId] = cachedMetadata;

            _batchStore[batchId] = new BatchStatusResponse
            {
                BatchId = batchId,
                Status = "Initialized",
                TotalFiles = filesToUpload.Count
            };

            return response;
        }

        public BatchStatusResponse GetStatus(Guid batchId)
        {
            if (_batchStore.TryGetValue(batchId, out var status))
            {
                return status;
            }

            return new BatchStatusResponse { BatchId = batchId, Status = "NotFound" };
        }

        public async Task<CompleteBatchResult> CompleteAsync(Guid batchId)
        {
            if (!_batchStore.TryGetValue(batchId, out var status) || !_batchFilesMetadata.TryGetValue(batchId, out var filesMetadata))
            {
                return new CompleteBatchResult { Success = false, Error = "Batch session identifier or metadata configuration not found." };
            }

            var targetStorageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "CompletedUploads");
            Directory.CreateDirectory(targetStorageDirectory);

            try
            {
                foreach (var fileInfo in filesMetadata)
                {
                    if (!_receivedChunks.TryGetValue(fileInfo.FieldId, out var chunkList) || chunkList.Count < fileInfo.ChunkCount)
                    {
                        return new CompleteBatchResult
                        {
                            Success = false,
                            Error = $"Cannot assemble '{fileInfo.FileName}'. Missing chunks. Received {chunkList?.Count ?? 0}/{fileInfo.ChunkCount}."
                        };
                    }

                    var finalFilePath = Path.Combine(targetStorageDirectory, fileInfo.FileName);
                    if (File.Exists(finalFilePath)) File.Delete(finalFilePath);

                    using (var finalFileStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                    {
                        for (int chunkIndex = 0; chunkIndex < fileInfo.ChunkCount; chunkIndex++)
                        {
                            var partPath = GetPartPath(fileInfo.FieldId, chunkIndex);

                            if (!File.Exists(partPath))
                            {
                                return new CompleteBatchResult { Success = false, Error = $"Chunk fragment file missing during sequential layout lookup: index {chunkIndex}" };
                            }

                            using (var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                await partStream.CopyToAsync(finalFileStream);
                            }
                        }
                    }

                    string actualFileMd5Hex;
                    using (var md5Evaluator = MD5.Create())
                    using (var assembledFileReadStream = new FileStream(finalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                    {
                        var rawHashBytes = await md5Evaluator.ComputeHashAsync(assembledFileReadStream);
                        actualFileMd5Hex = BitConverter.ToString(rawHashBytes).Replace("-", "").ToLowerInvariant();
                    }

                    if (actualFileMd5Hex != fileInfo.ExpectedMd5Hex)
                    {
                        if (File.Exists(finalFilePath)) File.Delete(finalFilePath);

                        return new CompleteBatchResult
                        {
                            Success = false,
                            Error = $"Security Verification Failed! Master MD5 mismatch on file '{fileInfo.FileName}'. Assembled hash does not match original signature registry client metrics."
                        };
                    }

                    var chunkDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileInfo.FieldId.ToString());
                    if (Directory.Exists(chunkDirectoryPath))
                    {
                        Directory.Delete(chunkDirectoryPath, true);
                    }

                    _receivedChunks.TryRemove(fileInfo.FieldId, out _);
                }

                status.Status = "Completed";
                _batchStore[batchId] = status;
                _batchFilesMetadata.TryRemove(batchId, out _);

                return new CompleteBatchResult { Success = true };
            }
            catch (Exception ex)
            {
                return new CompleteBatchResult { Success = false, Error = $"Assembly processing validation fault crashed: {ex.Message}" };
            }
        }

        private string GetPartPath(Guid fileId, int chunkIndex)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileId.ToString(), $"chunk_{chunkIndex}.part");
        }

        private void MarkReceived(Guid fileId, int chunkIndex)
        {
            var chunks = _receivedChunks.GetOrAdd(fileId, _ => new ConcurrentBag<int>());
            if (!chunks.Contains(chunkIndex))
            {
                chunks.Add(chunkIndex);
            }
        }
    }
}
