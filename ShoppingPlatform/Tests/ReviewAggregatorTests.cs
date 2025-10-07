using System.Collections.Generic;
using ShoppingPlatform.Models;
using ShoppingPlatform.Utilities;
using Xunit;

namespace ShoppingPlatform.Tests
{
    public class ReviewAggregatorTests
    {
        [Fact]
        public void ComputeApprovedAverage_NoReviews_ReturnsZero()
        {
            var (avg, count) = ReviewAggregator.ComputeApprovedAverage(new List<Review>());
            Assert.Equal(0.0, avg);
            Assert.Equal(0, count);
        }

        [Fact]
        public void ComputeApprovedAverage_OnlyUnapproved_ReturnsZero()
        {
            var reviews = new List<Review>
            {
                new Review { Rating = 5, Approved = false },
                new Review { Rating = 3, Approved = false }
            };
            var (avg, count) = ReviewAggregator.ComputeApprovedAverage(reviews);
            Assert.Equal(0.0, avg);
            Assert.Equal(0, count);
        }

        [Fact]
        public void ComputeApprovedAverage_ReturnsCorrectAverage()
        {
            var reviews = new List<Review>
            {
                new Review { Rating = 5, Approved = true },
                new Review { Rating = 3, Approved = true },
                new Review { Rating = 4, Approved = false }
            };
            var (avg, count) = ReviewAggregator.ComputeApprovedAverage(reviews);
            Assert.Equal(4.0, avg);
            Assert.Equal(2, count);
        }
    }
}
