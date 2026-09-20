using FileTransfer.Local;

namespace FileTransfer.ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Resumable Chunked Segment Hashing File Engine ===");

            Console.Write("Enter source file path (e.g. C:\\source\\large_file.bin): ");
            string sourceFilePath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(sourceFilePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Error] Source file path could not be discovered.");
                Console.ResetColor();
                return;
            }

            Console.Write("Enter destination path (e.g. D:\\destination\\): ");
            string destinationFolderPath = Console.ReadLine()?.Trim() ?? "";

            int chunkSize = 2 * 1024 * 1024;
            int maxConcurrencyStreams = 4;
            string serverBaseUrl = "http://localhost:5092";

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
                        apiBase: serverBaseUrl,
                        sourceFile: sourceFilePath,
                        destinationDir: destinationFolderPath,
                        chunkSize: chunkSize,
                        maxParallel: maxConcurrencyStreams
                    );
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Critical Session Crash] Error: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("\nExecution cycle finished. Press any key to exit terms...");
            Console.ReadKey();
        }
    }
}
