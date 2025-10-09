using System.Net;

namespace ShoppingPlatform.Models
{
    /// <summary>
    /// Unified API response structure for all endpoints.
    /// </summary>
    public class ApiResponse<T>
    {
        public bool Success { get; set; } = true;

        /// <summary>
        /// HTTP status code (enum) for better readability.
        /// </summary>
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>
        /// Friendly message to describe the outcome.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// The actual payload.
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// Validation or error details.
        /// </summary>
        public List<string>? Errors { get; set; }

        /// <summary>
        /// Optional pagination info.
        /// </summary>
        public PaginationResponse? Pagination { get; set; }

        public static ApiResponse<T> Ok(T data, string? message = null) =>
            new() { Success = true, Data = data, Message = message, Status = HttpStatusCode.OK };

        public static ApiResponse<T> Created(T data, string? message = null) =>
            new() { Success = true, Data = data, Message = message, Status = HttpStatusCode.Created };

        public static ApiResponse<T> Fail(string message, List<string>? errors = null, HttpStatusCode status = HttpStatusCode.BadRequest) =>
            new() { Success = false, Message = message, Errors = errors, Status = status };
    }

    public class PaginationResponse
    {
        private int _pageNumber = 1;
        private int _pageSize = 10;

        public int PageNumber { get => _pageNumber; set => _pageNumber = value <= 0 ? 1 : value; }
        public int PageSize { get => _pageSize; set => _pageSize = value <= 0 ? 10 : value; }
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
    }
}
