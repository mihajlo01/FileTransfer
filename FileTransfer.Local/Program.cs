using FileTransfer.Local.Client.Models;
using Microsoft.Extensions.Configuration;

namespace FileTransfer.Local
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== FileTransfer Local Console Client ===");

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var settings = config.GetSection("ClientSettings").Get<ClientSettings>() ?? new ClientSettings();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    settings.SourceFolder = args[i + 1];
                if (args[i].Equals("-destination", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    settings.DestinationFolder = args[i + 1];
            }

            if (string.IsNullOrWhiteSpace(settings.SourceFolder) || !Directory.Exists(settings.SourceFolder))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] Configured Source folder directory path is invalid or empty: '{settings.SourceFolder}'");
                Console.ResetColor();
                return;
            }

            string[] selectedFiles = Directory.GetFiles(settings.SourceFolder);
            if (selectedFiles.Length == 0)
            {
                Console.WriteLine($"[Warning] No files discovered inside folder tracking target: '{settings.SourceFolder}'");
                return;
            }

            int absoluteChunkSizeBytes = settings.ChunkSizeMB * 1024 * 1024;

            Console.WriteLine($"[Config Live] Target Server: {settings.ServerBaseUrl}");
            Console.WriteLine($"[Config Live] Processing Slices: {settings.ChunkSizeMB} MB | Max Concurrency: {settings.MaxConcurrency} streams");

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            using (var httpClient = new HttpClient(handler))
            {
                httpClient.Timeout = TimeSpan.FromMinutes(60);
                var transferEngine = new LocalFileTransfer();

                try
                {
                    await transferEngine.UploadFilesWithVerificationAsync(
                        httpClient: httpClient,
                        apiBase: settings.ServerBaseUrl,
                        sourceFile: selectedFiles[0],
                        destinationDir: settings.DestinationFolder,
                        chunkSize: absoluteChunkSizeBytes,
                        maxParallel: settings.MaxConcurrency
                    );

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[Success] Local model-bound file operation executed successfully!");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Error] Pipeline failure encountered: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("\nTask sequence complete. Press any key to release terminal window locks...");
            Console.ReadKey();
        }
    }
}
