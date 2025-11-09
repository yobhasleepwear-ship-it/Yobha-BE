using MongoDB.Driver;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Helpers
{
    public class GiftCardHelper
    {
        private readonly IMongoCollection<GiftCard> _giftCardCollection;
        private readonly ILogger _log;

        public GiftCardHelper(IMongoDatabase db, IMongoClient mongoClient,
                              ILogger log)
        {
            _giftCardCollection = db.GetCollection<GiftCard>("giftcards");
            _log = log;
        }

        private string GenerateGiftCardCode()
        {
            // deterministic but sufficiently random: GC + YYMM + 8 hex chars
            return $"GC{DateTime.UtcNow:yyMM}{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}";
        }

        // Create a gift card inside an existing session/transaction
        public async Task<GiftCard> CreateGiftCardAsync(IClientSessionHandle session, decimal amount, string currency, string issuedOrderId, string ownerUserId = null)
        {
            if (amount <= 0) throw new ArgumentException(nameof(amount));

            const int maxAttempts = 3;
            for (int i = 0; i < maxAttempts; i++)
            {
                var gc = new GiftCard
                {
                    GiftCardNumber = GenerateGiftCardCode(),
                    Balance = amount,
                    Currency = currency,
                    IsActive = true,
                    IssuedOrderId = issuedOrderId,
                    IssuedAt = DateTime.UtcNow,
                    OwnerUserId = ownerUserId
                };

                try
                {
                    await _giftCardCollection.InsertOneAsync(session, gc);                  

                    return gc;
                }
                catch (MongoWriteException mwx) when (mwx.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // collision on GiftCardNumber unique index; retry
                    if (i == maxAttempts - 1) throw;
                }
            }

            throw new InvalidOperationException("Unable to generate unique gift card number after multiple attempts.");
        }

        // Atomically deduct from gift card (not using session here – optional to pass session)
        // Returns the deduction amount actually applied (could be < requested if card had lower balance)
        public async Task<GiftCardDeductionResult> ApplyGiftCardAsync(IClientSessionHandle? session, string giftCardNumber, decimal maxToApply, string expectedCurrency = "INR")
        {
            if (maxToApply <= 0) return new GiftCardDeductionResult { Deducted = 0m };

            // find current card
            var card = await _giftCardCollection.Find(g => g.GiftCardNumber == giftCardNumber && g.IsActive).FirstOrDefaultAsync();
            if (card == null) throw new InvalidOperationException("Gift card not found or inactive");
            if (!string.Equals(card.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Gift card currency mismatch");

            decimal deduction = Math.Min(card.Balance, maxToApply);
            if (deduction <= 0) throw new InvalidOperationException("Gift card has no balance");

            // Build conditional filter to ensure atomicity
            var filter = Builders<GiftCard>.Filter.And(
                Builders<GiftCard>.Filter.Eq(g => g.GiftCardNumber, giftCardNumber),
                Builders<GiftCard>.Filter.Gte(g => g.Balance, deduction),
                Builders<GiftCard>.Filter.Eq(g => g.IsActive, true)
            );

            var update = Builders<GiftCard>.Update
                .Inc(g => g.Balance, -deduction)
                .Set(g => g.IsActive, card.Balance - deduction > 0)
                .Set(g => g.RedeemedAt, card.Balance - deduction <= 0 ? DateTime.UtcNow : (DateTime?)null);

            var options = new FindOneAndUpdateOptions<GiftCard> { ReturnDocument = ReturnDocument.After };

            GiftCard updated;
            if (session != null)
                updated = await _giftCardCollection.FindOneAndUpdateAsync(session, filter, update, options);
            else
                updated = await _giftCardCollection.FindOneAndUpdateAsync(filter, update, options);

            if (updated == null)
            {
                // race condition; caller can retry
                throw new InvalidOperationException("Unable to atomically deduct gift card (concurrent modification).");
            }


            return new GiftCardDeductionResult { Deducted = deduction, RemainingBalance = updated.Balance, GiftCardId = updated.Id };
        }

        public class GiftCardDeductionResult
        {
            public decimal Deducted { get; set; }
            public decimal RemainingBalance { get; set; }
            public string GiftCardId { get; set; } = string.Empty;
        }
    }

}
