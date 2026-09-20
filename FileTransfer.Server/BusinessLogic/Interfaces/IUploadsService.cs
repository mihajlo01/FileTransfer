using FileTransfer.Server.Models;

namespace FileTransfer.Server.BusinessLogic.Interfaces
{
    public interface IUploadsService
    {
        Task<bool> SaveChunkAsync(Guid fileId, int chunkIndex, Stream body, byte[] expectedMd5);

        CreateBatchResponse CreateBatch(CreateBatchRequest request, string userName);

        BatchStatusResponse GetStatus(Guid batchId);

        Task<CompleteBatchResult> CompleteAsync(Guid batchId);
    }
}