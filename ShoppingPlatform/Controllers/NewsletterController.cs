using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Models;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NewsletterController : ControllerBase
    {

        private readonly INewsletterService _newsletterService;
        public NewsletterController(INewsletterService newsletterService) => _newsletterService = newsletterService;

        [HttpPost]
        public async Task<IActionResult> Subscribe([FromBody] NewsletterCreateDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var created = await _newsletterService.AddAsync(dto, ct);
            return CreatedAtAction(nameof(Subscribe), new { id = created.Id }, created);
        }
    }
}
