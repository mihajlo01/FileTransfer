namespace FileTransfer.Local
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Resilient Configurable Local Console Client ===");

            string sourceDirectory = @"C:\YourSourceFolder";
            string serverBaseUrl = "http://localhost:5092";
            int configuredChunkSizeMB = 8;
            int maxRetriesCount = 3;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    sourceDirectory = args[i + 1];
                if (args[i].Equals("-size", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out configuredChunkSizeMB);
                if (args[i].Equals("-retries", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out maxRetriesCount);
            }

            int absoluteChunkSizeBytes = configuredChunkSizeMB * 1024 * 1024;

            if (!Directory.Exists(sourceDirectory))
            {
                Console.WriteLine($"[Error] Source folder directory path could not be resolved: '{sourceDirectory}'");
                return;
            }

            string[] selectedFiles = Directory.GetFiles(sourceDirectory);
            if (selectedFiles.Length == 0)
            {
                Console.WriteLine($"[Warning] No files discovered inside tracking target route folder: '{sourceDirectory}'");
                return;
            }

            Console.WriteLine($"[Config] Slicing chunks at: {configuredChunkSizeMB} MB | Max Retries: {maxRetriesCount}");

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            using (var httpClient = new HttpClient(handler))
            {
                httpClient.Timeout = TimeSpan.FromMinutes(30);
                var transferEngine = new LocalFileTransfer();

                try
                {
                    await transferEngine.UploadFilesAsync(
                        httpClient: httpClient,
                        apiBase: serverBaseUrl,
                        paths: selectedFiles,
                        chunkSize: absoluteChunkSizeBytes,
                        maxParallel: 4,
                        maxRetries: maxRetriesCount
                    );

                    Console.WriteLine("\n[Success] Local batch operation finished successfully!");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Critical Error] Session broke: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("Press any key to close...");
            Console.ReadKey();
        }
    }
}
