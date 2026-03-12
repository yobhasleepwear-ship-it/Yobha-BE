using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Services
{
    public class CartAbandonMonitorService : BackgroundService
    {
        private readonly ILogger<CartAbandonMonitorService> _logger;
        private readonly IMongoCollection<CartItem> _cartCollection;
        private readonly IMongoCollection<CartAbandonAudit> _auditCollection;
        private readonly UserRepository _userRepository;
        private readonly IBrevoCrmService _brevo;
        private readonly BrevoSettings _settings;

        public CartAbandonMonitorService(
            IMongoDatabase db,
            UserRepository userRepository,
            IBrevoCrmService brevo,
            IOptions<BrevoSettings> options,
            ILogger<CartAbandonMonitorService> logger)
        {
            _cartCollection = db.GetCollection<CartItem>("Cart");
            _auditCollection = db.GetCollection<CartAbandonAudit>("cartAbandonAudit");
            _userRepository = userRepository;
            _brevo = brevo;
            _settings = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_settings.Enabled)
                    {
                        await ScanAndNotifyAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cart abandon monitor iteration failed");
                }

                var interval = Math.Max(1, _settings.CartAbandonScanIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
            }
        }

        private async Task ScanAndNotifyAsync(CancellationToken ct)
        {
            var abandonAfter = Math.Max(1, _settings.CartAbandonAfterMinutes);
            var cutoffUtc = DateTime.UtcNow.AddMinutes(-abandonAfter);
            var sentCount = 0;
            var skippedCount = 0;

            // Group by user and inspect latest cart update time.
            var grouped = await _cartCollection.Aggregate()
                .Match(c => c.UpdatedAt <= cutoffUtc)
                .Group(c => c.UserId, g => new
                {
                    UserId = g.Key,
                    LastCartUpdatedAt = g.Max(x => x.UpdatedAt)
                })
                .ToListAsync(ct);

            foreach (var candidate in grouped)
            {
                if (string.IsNullOrWhiteSpace(candidate.UserId)) continue;

                var audit = await _auditCollection
                    .Find(x => x.UserId == candidate.UserId)
                    .FirstOrDefaultAsync(ct);

                // If we've already notified for this cart state (or newer), skip.
                if (audit != null && audit.LastCartUpdatedAt >= candidate.LastCartUpdatedAt)
                {
                    skippedCount++;
                    continue;
                }

                var user = await _userRepository.GetByIdAsync(candidate.UserId);
                if (user == null || string.IsNullOrWhiteSpace(user.Email))
                {
                    skippedCount++;
                    continue;
                }

                var cartItems = await _cartCollection.Find(c => c.UserId == candidate.UserId).ToListAsync(ct);
                if (cartItems.Count == 0)
                {
                    skippedCount++;
                    continue;
                }

                var sent = await _brevo.TrackCartAbandonedAsync(user, cartItems, candidate.LastCartUpdatedAt, ct);
                if (!sent)
                {
                    _logger.LogWarning("Brevo cart-abandon send failed for user {UserId}; will retry on next scan", candidate.UserId);
                    skippedCount++;
                    continue;
                }

                var upsert = Builders<CartAbandonAudit>.Update
                    .Set(x => x.UserId, candidate.UserId)
                    .Set(x => x.LastCartUpdatedAt, candidate.LastCartUpdatedAt)
                    .Set(x => x.LastNotifiedAt, DateTime.UtcNow);

                await _auditCollection.UpdateOneAsync(
                    x => x.UserId == candidate.UserId,
                    upsert,
                    new UpdateOptions { IsUpsert = true },
                    ct);

                sentCount++;
            }

            _logger.LogInformation(
                "Cart-abandon scan done. Cutoff={CutoffUtc} Candidates={CandidateCount} Sent={SentCount} Skipped={SkippedCount}",
                cutoffUtc, grouped.Count, sentCount, skippedCount);
        }
    }
}
