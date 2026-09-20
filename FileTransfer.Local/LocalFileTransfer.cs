using FileTransfer.Server.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace FileTransfer.Local
{
    public class LocalFileTransfer
    {
        public async Task UploadFilesWithVerificationAsync(HttpClient httpClient, string apiBase, string sourceFile, string destinationDir, int chunkSize, int maxParallel)
        {
            var fileInfo = new FileInfo(sourceFile);
            long totalFileLength = fileInfo.Length;

            Console.WriteLine("[Client] Calculating source file signature hash strings...");
            string sourceFileSha256 = Sha256HexFile(sourceFile);

            var fileRequest = new CreateFileRequest
            {
                FileName = fileInfo.Name,
                SizeBytes = totalFileLength,
                Md5Hex = "legacy_skip",
                ChunkSizeBytes = chunkSize
            };

            var batchPayload = new CreateBatchRequest
            {
                DestinationFolder = destinationDir,
                Files = new List<CreateFileRequest> { fileRequest }
            };

            var createResponse = await httpClient.PostAsJsonAsync($"{apiBase}/api/uploads/batches", batchPayload);
            createResponse.EnsureSuccessStatusCode();

            var batch = await createResponse.Content.ReadFromJsonAsync<CreateBatchResponse>();
            if (batch == null || batch.Files == null) throw new Exception("Failed to map blueprint configuration registries.");

            var remoteFileInfo = batch.Files.First();
            Console.WriteLine("\n=== Transmitting Chunk Fragments Sequences ===");

            using (var gate = new SemaphoreSlim(maxParallel))
            {
                var tasks = new List<Task>();

                for (int i = 0; i < remoteFileInfo.ChunkCount; i++)
                {
                    int indexId = i;
                    long blockOffsetPosition = (long)indexId * chunkSize;

                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            await PutChunkWithAutoRetryAsync(httpClient, apiBase, sourceFile, remoteFileInfo.FieldId, indexId, blockOffsetPosition, chunkSize);
                        }
                        finally { gate.Release(); }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            Console.WriteLine("\n[Client] All chunks dispatched. Orchestrating master SHA-256 signature verification pass...");

            var doneResponse = await httpClient.PostAsync($"{apiBase}/api/uploads/batches/{batch.BatchId}/complete", null);
            doneResponse.EnsureSuccessStatusCode();

            var completionResult = await doneResponse.Content.ReadFromJsonAsync<CompleteBatchResult>();
            if (completionResult == null || !completionResult.Success)
            {
                throw new Exception($"Server assembly verification pipeline failed: {completionResult?.Error}");
            }

            string destinationFileSha256 = completionResult.Error?.ToLowerInvariant().Trim() ?? string.Empty;

            Console.WriteLine("\n=======================================================");
            Console.WriteLine("=== MASTER INTEGRITY CHECKSUM REPORT ===");
            Console.WriteLine("=======================================================");
            Console.WriteLine($"Source Complete File Hash (SHA256):      {sourceFileSha256}");
            Console.WriteLine($"Destination Complete File Hash (SHA256): {destinationFileSha256}");
            Console.WriteLine("=======================================================");

            if (sourceFileSha256.Trim() == destinationFileSha256)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: Master file hashes match exactly. Zero data corruption verified!");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: Master file signature validation mismatch! Integrity error.");
                Console.ResetColor();
            }
        }

        private async Task PutChunkWithAutoRetryAsync(HttpClient httpClient, string apiBase, string path, Guid fileId, int index, long position, int chunkSize)
        {
            int failureCount = 0;
            while (true)
            {
                try
                {
                    long offset = (long)index * chunkSize;
                    int length;
                    byte[] buffer;

                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                    {
                        fs.Position = offset;
                        length = (int)Math.Min(chunkSize, fs.Length - offset);
                        buffer = new byte[length];
                        int read = 0;
                        while (read < length)
                            read += await fs.ReadAsync(buffer, read, length - read);
                    }

                    string md5B64;
                    string md5HexDisplay;
                    using (var md5 = MD5.Create())
                    {
                        var rawHash = md5.ComputeHash(buffer);
                        md5B64 = Convert.ToBase64String(rawHash);
                        md5HexDisplay = BitConverter.ToString(rawHash).Replace("-", "").ToLowerInvariant();
                    }

                    using (var content = new ByteArrayContent(buffer))
                    {
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        content.Headers.Add("Content-MD5", md5B64);

                        var resp = await httpClient.PutAsync($"{apiBase}/api/uploads/files/{fileId}/chunks/{index}", content);

                        resp.EnsureSuccessStatusCode();
                    }

                    Console.WriteLine($") position = {position.ToString().PadRight(12)} block index = {index.ToString().PadRight(4)} hash (MD5) = {md5HexDisplay}");
                    return;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Retry Alert] Block {index} at position {position} failed verification validation parameters ({ex.Message}). Auto-resubmitting attempt {failureCount}...");
                    Console.ResetColor();
                    await Task.Delay(1000 * failureCount);
                }
            }
        }

        private static string Sha256HexFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
    }
}
