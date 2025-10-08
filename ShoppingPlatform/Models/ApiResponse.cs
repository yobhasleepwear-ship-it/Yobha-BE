﻿namespace ShoppingPlatform.Models
{
    // Models/ApiResponse.cs
    public class ApiResponse<T>
    {
        public bool Success { get; set; } = true;
        public string? Message { get; set; }
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }

        public static ApiResponse<T> Ok(T data, string? message = null) =>
            new() { Success = true, Data = data, Message = message };

        public static ApiResponse<T> Fail(string message, List<string>? errors = null) =>
            new() { Success = false, Message = message, Errors = errors };
    }

}
