using System.Collections.Generic;
using System.Linq;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Utilities
{
    public static class ReviewAggregator
    {
        public static (double average, int count) ComputeApprovedAverage(IEnumerable<Review> reviews)
        {
            var approved = (reviews ?? Enumerable.Empty<Review>()).Where(r => r.Approved).ToList();
            var count = approved.Count;
            var avg = count > 0 ? approved.Average(r => r.Rating) : 0.0;
            return (avg, count);
        }
    }
}
