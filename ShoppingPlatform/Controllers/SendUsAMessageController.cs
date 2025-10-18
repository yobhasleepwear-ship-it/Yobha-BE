using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SendUsAMessageController : ControllerBase
    {
        private readonly IMongoCollection<SendUsAMessage> _messages;

        public SendUsAMessageController(IMongoDatabase db)
        {
            _messages = db.GetCollection<SendUsAMessage>("SendUsAMessages");

            // Create an index for efficient sorting (optional)
            var indexKeys = Builders<SendUsAMessage>.IndexKeys.Descending(x => x.Created);
            var indexModel = new CreateIndexModel<SendUsAMessage>(indexKeys);
            _messages.Indexes.CreateOne(indexModel);
        }

        /// <summary>
        /// Public endpoint to submit a message (no auth).
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> CreateAsync([FromBody] SendUsAMessage input, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var doc = new SendUsAMessage
            {
                FullName = input.FullName?.Trim(),
                Email = input.Email?.Trim(),
                Subject = input.Subject?.Trim(),
                PhoneNumber = input.PhoneNumber?.Trim(),
                Message = input.Message?.Trim(),
                Created = DateTime.UtcNow
            };

            await _messages.InsertOneAsync(doc, cancellationToken: ct);

            return Ok(new { success = true, message = "Message sent successfully." });
        }

        /// <summary>
        /// Admin-only: Get messages paginated, sorted by Created (newest first).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> GetPageAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sort = "desc", // "desc" (default) or "asc"
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 100) pageSize = 100;

            var filter = Builders<SendUsAMessage>.Filter.Empty;
            var sortDef = sort?.ToLowerInvariant() == "asc"
                ? Builders<SendUsAMessage>.Sort.Ascending(x => x.Created)
                : Builders<SendUsAMessage>.Sort.Descending(x => x.Created);

            var totalItems = await _messages.CountDocumentsAsync(filter, cancellationToken: ct);

            var items = await _messages.Find(filter)
                .Sort(sortDef)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(ct);

            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            return Ok(new
            {
                success = true,
                data = items,
                pagination = new
                {
                    page,
                    pageSize,
                    totalItems,
                    totalPages,
                    sort = sort?.ToLowerInvariant()
                }
            });
        }
    }
}
