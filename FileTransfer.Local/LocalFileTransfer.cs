using FileTransfer.Server.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace FileTransfer.Local
{
    public class LocalFileTransfer
    {
        public async Task UploadFilesAsync(HttpClient httpClient, string apiBase, string[] paths, int chunkSize, int maxParallel, int maxRetries)
        {
            var filesRequest = paths.Select(p => new CreateFileRequest
            {
                FileName = Path.GetFileName(p),
                SizeBytes = new FileInfo(p).Length,
                Md5Hex = Md5HexFile(p),
                ChunkSizeBytes = chunkSize
            }).ToList();

            var createResponse = await httpClient.PostAsJsonAsync($"{apiBase}/api/uploads/batches", new CreateBatchRequest { Files = filesRequest });
            createResponse.EnsureSuccessStatusCode();

            var batch = await createResponse.Content.ReadFromJsonAsync<CreateBatchResponse>();
            if (batch == null || batch.Files == null) throw new Exception("Failed to deserialize blueprint configuration map registry entries.");

            using (var gate = new SemaphoreSlim(maxParallel))
            {
                var tasks = new List<Task>();
                foreach (var remote in batch.Files)
                {
                    var local = paths.First(p => Path.GetFileName(p) == remote.FileName);

                    for (int i = 0; i < remote.ChunkCount; i++)
                    {
                        int idx = i;
                        tasks.Add(Task.Run(async () =>
                        {
                            await gate.WaitAsync();
                            try
                            {
                                await PutChunkWithRetryAsync(httpClient, apiBase, local, remote.FieldId, idx, chunkSize, maxRetries);
                            }
                            finally { gate.Release(); }
                        }));
                    }
                }
                await Task.WhenAll(tasks);
            }

            var done = await httpClient.PostAsync($"{apiBase}/api/uploads/batches/{batch.BatchId}/complete", null);
            done.EnsureSuccessStatusCode();
        }

        private async Task PutChunkWithRetryAsync(HttpClient httpClient, string apiBase, string path, Guid fileId, int index, int chunkSize, int maxRetries)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    await ExecutePutChunkAsync(httpClient, apiBase, path, fileId, index, chunkSize);
                    return;
                }
                catch (Exception ex)
                {
                    attempts++;
                    if (attempts > maxRetries)
                    {
                        throw new Exception($"Chunk allocation {index} definitively failed after {maxRetries} attempts. Internal: {ex.Message}", ex);
                    }

                    int delayMs = (int)Math.Pow(2, attempts) * 1000;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Retry Warn] Chunk index {index} failed ({ex.Message}). Retrying attempt {attempts}/{maxRetries} in {delayMs}ms...");
                    Console.ResetColor();

                    await Task.Delay(delayMs);
                }
            }
        }

        private async Task ExecutePutChunkAsync(HttpClient httpClient, string apiBase, string path, Guid fileId, int index, int chunkSize)
        {
            long offset = (long)index * chunkSize;
            int len;
            byte[] buffer;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
            {
                fs.Position = offset;
                len = (int)Math.Min(chunkSize, fs.Length - offset);
                buffer = new byte[len];
                int read = 0;
                while (read < len)
                    read += await fs.ReadAsync(buffer, read, len - read);
            }

            string md5B64;
            using (var md5 = MD5.Create())
                md5B64 = Convert.ToBase64String(md5.ComputeHash(buffer));

            using (var content = new ByteArrayContent(buffer))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Headers.Add("Content-MD5", md5B64);

                var resp = await httpClient.PutAsync($"{apiBase}/api/uploads/files/{fileId}/chunks/{index}", content);
                resp.EnsureSuccessStatusCode();
            }
        }

        private string Md5HexFile(string path)
        {
            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
    }
}
