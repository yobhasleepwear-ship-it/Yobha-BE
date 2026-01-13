using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Dto;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Helpers;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Services
{
    public class BuybackService : IBuybackService
    {
        private readonly IMongoCollection<BuybackRequest> _buybackCollection;
        private readonly IMongoCollection<User> _userCollection;
        private readonly IMongoClient _mongoClient;
        private readonly PaymentHelper _paymentHelper;
        private readonly ILoyaltyPointAuditService _loyaltyPointAuditService;

        public BuybackService(IMongoDatabase database, IMongoClient mongoClient, PaymentHelper paymentHelper, ILoyaltyPointAuditService loyaltyPointAuditService)
        {
            _buybackCollection = database.GetCollection<BuybackRequest>("Buyback");
            _userCollection = database.GetCollection<User>("users");
            _mongoClient = mongoClient;
            _paymentHelper = paymentHelper;
            _loyaltyPointAuditService = loyaltyPointAuditService;
        }

        /// <summary>
        /// Create a new buyback request.
        /// </summary>
        public async Task<BuybackRequest> CreateBuybackAsync(BuybackRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProductId))
                throw new ArgumentException("ProductId is required.");

            request.CreatedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;

            await _buybackCollection.InsertOneAsync(request);
            return request;
        }

        /// <summary>
        /// Get all buybacks for the logged-in user sorted by creation time.
        /// </summary>
        public async Task<IEnumerable<BuybackRequest>> GetBuybacksByUserAsync(string userId)
        {
            var filter = Builders<BuybackRequest>.Filter.Eq(x => x.UserId, userId);
            var sort = Builders<BuybackRequest>.Sort.Descending(x => x.CreatedAt);

            return await _buybackCollection.Find(filter).Sort(sort).ToListAsync();
        }


        public async Task<PagedResult<BuybackRequest>> GetBuybackDetailsAsync(string? orderId, string? productId, string? buybackId, int page = 1, int size = 20)
        {
            // Build dynamic filters only for provided params
            var filters = new List<FilterDefinition<BuybackRequest>>();
            var builder = Builders<BuybackRequest>.Filter;

            if (!string.IsNullOrWhiteSpace(buybackId))
                filters.Add(builder.Eq(b => b.Id, buybackId));

            if (!string.IsNullOrWhiteSpace(orderId))
                filters.Add(builder.Eq(b => b.OrderId, orderId));

            if (!string.IsNullOrWhiteSpace(productId))
                filters.Add(builder.Eq(b => b.ProductId, productId));

            FilterDefinition<BuybackRequest> finalFilter = filters.Count == 0 ? builder.Empty : builder.And(filters);

            // Pagination
            page = Math.Max(1, page);
            size = Math.Clamp(size, 1, 100);
            var skip = (page - 1) * (long)size;

            // Total count (fast enough; if very large collection consider using estimated count or limit)
            var total = await _buybackCollection.CountDocumentsAsync(finalFilter);

            // Get paged items sorted by CreatedAt desc
            var sort = Builders<BuybackRequest>.Sort.Descending(b => b.CreatedAt);

            var items = await _buybackCollection
                .Find(finalFilter)
                .Sort(sort)
                .Skip((int)skip)
                .Limit(size)
                .ToListAsync();

            return new PagedResult<BuybackRequest>
            {
                Items = items,
                Page = page,
                PageSize = size,
                TotalCount = total,
            };
        }


        public async Task<BuybackRequest> AdminApproveOrUpdateBuybackAsync(AdminUpdateBuybackRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BuybackId))
                throw new ArgumentException("BuybackId is required.");

            var filter = Builders<BuybackRequest>.Filter.Eq(b => b.Id, request.BuybackId);
            var existing = await _buybackCollection.Find(filter).FirstOrDefaultAsync();
            if (existing == null)
                throw new KeyNotFoundException("Buyback not found");

            // Determine type
            var reqType = existing.RequestType?.Trim()?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(reqType))
                throw new InvalidOperationException("Buyback request type is not set.");

            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();

            try
            {
                // TRADEIN / RECYCLE => award loyalty points
                if (reqType == "tradein" || reqType == "recycle")
                {
                    var pts = request.LoyaltyPoints ?? existing.LoyaltyPoints ?? 0m;
                    if (pts <= 0) throw new ArgumentException("LoyaltyPoints must be provided and greater than zero for TradeIn/Recycle approvals.");

                    // Update buyback doc
                    var buybackUpdate = Builders<BuybackRequest>.Update
                        .Set(b => b.LoyaltyPoints, pts)
                        .Set(b => b.BuybackStatus, "approved")
                        .Set(b => b.UpdatedAt, DateTime.UtcNow);
                    await _buybackCollection.UpdateOneAsync(session, filter, buybackUpdate);

                    // Credit user loyalty points with $inc
                    var userFilter = Builders<User>.Filter.Eq(u => u.Id, existing.UserId);
                    var userUpdate = Builders<User>.Update.Inc(u => u.LoyaltyPoints, pts);
                    var user = await _userCollection.FindOneAndUpdateAsync(session, userFilter, userUpdate, new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });

                    if (user == null)
                    {
                        await session.AbortTransactionAsync();
                        throw new KeyNotFoundException($"User {existing.UserId} not found to credit loyalty points.");
                    }
                    var updateloyalty = _loyaltyPointAuditService.RecordSimpleAsync(existing.UserId, "Credit", pts, "BuyBack", null, user.Email, user.PhoneNumber, user.LoyaltyPoints);

                    await session.CommitTransactionAsync();

                    // return the fresh copy
                    var updated = await _buybackCollection.Find(filter).FirstOrDefaultAsync();
                    return updated!;
                }

                // REPAIRREUSE => admin must provide amount
                if (reqType == "repairreuse")
                {
                    var amount = request.Amount ?? existing.Amount;
                    if (amount == null || amount <= 0) throw new ArgumentException("Amount must be provided and greater than zero for RepairReuse approval.");

                    var currency = request.Currency ?? existing.Currency ?? "INR";
                    var paymentMethod = (request.PaymentMethod ?? existing.PaymentMethod ?? "razorpay").ToLowerInvariant();

                    var buybackUpdate = Builders<BuybackRequest>.Update
                        .Set(b => b.Amount, amount)
                        .Set(b => b.Currency, currency)
                        .Set(b => b.PaymentMethod, paymentMethod)
                        .Set(b => b.BuybackStatus, "approved")
                        .Set(b => b.PaymentStatus, "Pending")
                        .Set(b => b.UpdatedAt, DateTime.UtcNow);

                    await _buybackCollection.UpdateOneAsync(session, filter, buybackUpdate);
                    await session.CommitTransactionAsync();

                    var updated = await _buybackCollection.Find(filter).FirstOrDefaultAsync();
                    return updated!;
                }

                // Unknown type
                await session.AbortTransactionAsync();
                throw new InvalidOperationException("Unsupported RequestType for admin update.");
            }
            catch
            {
                await session.AbortTransactionAsync();
                throw;
            }
        }

        /// <summary>
        /// Called by user to initiate payment for RepairReuse buyback.
        /// Creates Razorpay order via PaymentHelper and persists RazorpayOrderId.
        /// Returns an object (Razorpay order id + any required details) suitable for frontend.
        /// </summary>
        public async Task<object> InitiateBuybackPaymentAsync(string buybackId, string userId)
        {
            var filter = Builders<BuybackRequest>.Filter.Eq(b => b.Id, buybackId);
            var existing = await _buybackCollection.Find(filter).FirstOrDefaultAsync();
            if (existing == null)
                throw new KeyNotFoundException("Buyback not found.");   

            if (existing.RequestType?.Trim().ToLowerInvariant() != "repairreuse")
                throw new InvalidOperationException("Payment only supported for RepairReuse requests.");

            if (existing.Amount == null || existing.Amount <= 0)
                throw new InvalidOperationException("Buyback amount is not set. Please wait for admin to set the amount.");

            if ((existing.PaymentMethod ?? "razorpay").ToLowerInvariant() != "razorpay")
                throw new InvalidOperationException("Buyback payment method is not razorpay.");

            // If a reserve has already been created, return existing razorpay order id (idempotency)
            if (!string.IsNullOrWhiteSpace(existing.RazorpayOrderId))
            {
                return new
                {
                    existing.RazorpayOrderId,
                    Message = "Razorpay order already created for this buyback."
                };
            }

            // Create razorpay order
            // Use a deterministic order id for receipt/traceability
            string receiptId = $"buyback_{existing.Id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var rpResult = await _paymentHelper.CreateRazorpayOrderAsync(receiptId, existing.Amount.Value, existing.Currency ?? "INR", isInternational: false);
            if (!rpResult.Success)
                throw new InvalidOperationException($"Failed to create Razorpay order: {rpResult.ErrorMessage}");

            // Persist razorpay id to buyback
            var update = Builders<BuybackRequest>.Update
                .Set(b => b.RazorpayOrderId, rpResult.RazorpayOrderId)
                .Set(b => b.PaymentStatus, "ReserveCreated")
                .Set(b => b.UpdatedAt, DateTime.UtcNow)
                .Set(b => b.PaymentGatewayResponse, rpResult.ResponseBody);

            var updated = await _buybackCollection.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<BuybackRequest> { ReturnDocument = ReturnDocument.After });
            if (updated == null)
                throw new KeyNotFoundException("Buyback not found after creating payment reserve.");

            // Return the important info for frontend to call razorpay checkout
            return new
            {
                RazorpayOrderId = updated.RazorpayOrderId,
                Amount = updated.Amount,
                Currency = updated.Currency,
                Message = "Razorpay order created successfully."
            };
        }

        public async Task<bool> UpdateDeliveryDetailsAsync(
    string referenceId,
    DeliveryDetails request)
        {
            var delivery = new DeliveryDetails
            {
                Awb = request.Awb, // only if present
                Courier = "DELHIVERY",
                IsCod = request.IsCod,
                CodAmount = request.IsCod ? request.CodAmount : 0,
                IsInternational = request.IsInternational,
                Status = "READY_TO_SHIP",
                Type = request.Type,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var update = Builders<BuybackRequest>.Update
                .Set(x => x.deliveryDetails, delivery)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await _buybackCollection.UpdateOneAsync(
                x => x.Id == referenceId,
                update);

            return result.MatchedCount > 0;
        }

        public async Task<bool> UpdateDeliveryStatusAsync(
    string orderId,
    string status)
        {
            var update = Builders<BuybackRequest>.Update
                .Set(x => x.deliveryDetails.Status, status)
                .Set(x => x.deliveryDetails.UpdatedAt, DateTime.UtcNow);

            var result = await _buybackCollection.UpdateOneAsync(
                x => x.Id == orderId,
                update);

            return result.MatchedCount > 0;
        }
        public async Task<bool> UpdateDeliveryStatusAsync(UpdateOrderStatusAdmin request)
        {
            var updates = new List<UpdateDefinition<BuybackRequest>>();

            // deliveryDetails updates (only if orderStatus present)
            if (!string.IsNullOrWhiteSpace(request.orderStatus))
            {
                updates.Add(Builders<BuybackRequest>.Update
                    .Set(x => x.deliveryDetails.Status, request.orderStatus)
                    .Set(x => x.deliveryDetails.UpdatedAt, DateTime.UtcNow));
            }

            // payment status update (only if paymentStatus present)
            if (!string.IsNullOrWhiteSpace(request.paymentStatus))
            {
                updates.Add(Builders<BuybackRequest>.Update
                    .Set(x => x.PaymentStatus, request.paymentStatus)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
            }

            // nothing to update
            if (!updates.Any())
                return false;

            var update = Builders<BuybackRequest>.Update.Combine(updates);

            var result = await _buybackCollection.UpdateOneAsync(
                x => x.Id == request.id,
                update
            );

            return result.MatchedCount > 0;
        }

        public async Task<BuybackRequest?> GetByAwbAsync(string awb)
        {
            if (string.IsNullOrWhiteSpace(awb))
                return null;

            return await _buybackCollection.Find(x =>
                x.deliveryDetails != null &&
                x.deliveryDetails.Awb == awb
            ).FirstOrDefaultAsync();
        }

    }
}
