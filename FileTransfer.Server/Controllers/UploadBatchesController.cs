using FileTransfer.Server.BusinessLogic.Interfaces;
using FileTransfer.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace filetransfer.Server.Controllers
{
    [ApiController]
    [Route("api/uploads/batches")]
    public class UploadBatchesController : ControllerBase
    {
        private readonly IUploadsService _uploads;

        public UploadBatchesController(IUploadsService uploads)
        {
            _uploads = uploads;
        }

        [HttpPost("")]
        public IActionResult Create([FromBody] CreateBatchRequest request)
        {
            if (request == null || request.Files == null || request.Files.Count == 0)
            {
                return BadRequest("Batch request must contain at least one file configuration entry.");
            }

            var userName = User.Identity?.Name ?? "Anonymous";

            CreateBatchResponse batchResponse = _uploads.CreateBatch(request, userName);
            return Ok(batchResponse);
        }

        [HttpGet("{batchId:guid}/status")]
        public IActionResult Status(Guid batchId)
        {
            var status = _uploads.GetStatus(batchId);
            return Ok(status);
        }

        [HttpPost("{batchId:guid}/complete")]
        public async Task<IActionResult> Complete(Guid batchId)
        {
            var result = await _uploads.CompleteAsync(batchId);
            if (!result.Success)
            {
                return BadRequest(result.Error);
            }
            return Ok(result);
        }
    }
}