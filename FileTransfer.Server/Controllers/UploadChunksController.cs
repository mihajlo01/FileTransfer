using FileTransfer.Server.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FileTransfer.Server.Controllers
{
    [ApiController]
    [Route("api/uploads/files")]
    public class UploadChunksController : ControllerBase
    {
        private readonly IUploadsService _uploads;

        public UploadChunksController(IUploadsService uploads)
        {
            _uploads = uploads;
        }

        [HttpPut("{fileId:guid}/chunks/{chunkIndex:int}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> PutChunk(Guid fileId, int chunkIndex)
        {
            if (!Request.Headers.TryGetValue("Content-MD5", out var md5HeaderValues) ||
                string.IsNullOrEmpty(md5HeaderValues.FirstOrDefault()))
            {
                return BadRequest("Content-MD5 required!");
            }

            byte[] expected;
            try
            {
                expected = Convert.FromBase64String(md5HeaderValues.First()!);
            }
            catch (FormatException)
            {
                return BadRequest("Invalid Base64 format for Content-MD5 header.");
            }

            var bodyStream = Request.Body;

            var ok = await _uploads.SaveChunkAsync(fileId, chunkIndex, bodyStream, expected);
            if (!ok)
            {
                return BadRequest("Chunk MD5 mismatch!");
            }

            return NoContent();
        }
    }
}