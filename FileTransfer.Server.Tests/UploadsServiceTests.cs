using FileTransfer.Server.BusinessLogic.Services;
using FileTransfer.Server.Models;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace FileTransfer.Server.Tests
{
    public class UploadsServiceTests : IDisposable
    {
        private readonly UploadsService _service;
        private readonly UploadSettings _mockSettings;
        private readonly string _testTempDir;

        public UploadsServiceTests()
        {
            _testTempDir = Path.Combine(Directory.GetCurrentDirectory(), "TestUploads_Cache");

            _mockSettings = new UploadSettings
            {
                DefaultChunkSize = 2 * 1024 * 1024,
                MaxParallelChunks = 4,
                TemporaryUploadsFolder = _testTempDir,
                DefaultCompletedFolder = "TestCompleted_Cache",
                EnableMasterShaValidation = true
            };

            var optionsMock = new Mock<IOptions<UploadSettings>>();
            optionsMock.Setup(o => o.Value).Returns(_mockSettings);

            _service = new UploadsService(optionsMock.Object);
        }

        [Fact]
        public void CreateBatch_ShouldCalculateCorrectChunkCount_WhenFileSplitsUnevenly()
        {
            var request = new CreateBatchRequest
            {
                DestinationFolder = "TestOut",
                Files = new List<CreateFileRequest>
                {
                    new CreateFileRequest { FileName = "large_video.mp4", SizeBytes = 5 * 1024 * 1024, ChunkSizeBytes = 2 * 1024 * 1024 }
                }
            };

            var response = _service.CreateBatch(request, "TestUser");

            Assert.NotNull(response);
            Assert.Single(response.Files);
            Assert.Equal(3, response.Files[0].ChunkCount);
            Assert.Equal("large_video.mp4", response.Files[0].FileName);
        }

        [Fact]
        public async Task SaveChunkAsync_ShouldReturnTrue_WhenIncomingMd5MatchesPayload()
        {
            var fileId = Guid.NewGuid();
            var chunkIndex = 0;
            var rawTextData = "This is some test binary data payload stream sequence.";
            var dataBytes = Encoding.UTF8.GetBytes(rawTextData);

            byte[] expectedMd5;
            using (var md5 = MD5.Create())
            {
                expectedMd5 = md5.ComputeHash(dataBytes);
            }

            using (var memoryStream = new MemoryStream(dataBytes))
            {
                var result = await _service.SaveChunkAsync(fileId, chunkIndex, memoryStream, expectedMd5);

                Assert.True(result, "Service should accept chunk when MD5 hash matches perfectly.");

                var expectedPartPath = Path.Combine(_testTempDir, fileId.ToString(), $"chunk_{chunkIndex}.part");
                Assert.True(File.Exists(expectedPartPath), "Temporary part fragment chunk file should exist on disk layout.");
            }
        }

        [Fact]
        public async Task SaveChunkAsync_ShouldReturnFalse_WhenIncomingMd5IsCorrupted()
        {
            var fileId = Guid.NewGuid();
            var chunkIndex = 0;
            var dataBytes = Encoding.UTF8.GetBytes("Authentic uncorrupted content bytes.");
            var fakeCorruptedMd5 = new byte[16];

            using (var memoryStream = new MemoryStream(dataBytes))
            {
                var result = await _service.SaveChunkAsync(fileId, chunkIndex, memoryStream, fakeCorruptedMd5);

                Assert.False(result, "Service must reject chunk operations if computational hashes mismatch.");

                var partPath = Path.Combine(_testTempDir, fileId.ToString(), $"chunk_{chunkIndex}.part");
                Assert.False(File.Exists(partPath), "Corrupted data fragments should be completely expunged from filesystem.");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
    }
}