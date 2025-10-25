using Microsoft.AspNetCore.Authorization; // <-- ensure you have auth in pipeline
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShoppingPlatform.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CareersController : ControllerBase
    {
        private readonly IJobPostingRepository _jobRepo;
        private readonly IApplicantRepository _appRepo;

        public CareersController(IJobPostingRepository jobRepo, IApplicantRepository appRepo)
        {
            _jobRepo = jobRepo;
            _appRepo = appRepo;
        }

        // ============================
        // Public endpoints
        // ============================

        // GET api/careers
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string department = null)
        {
            var jobs = await _jobRepo.GetAllAsync();
            // filter and return only Active and not expired
            var result = jobs
                .Where(j => j.Status == "Active" && j.ApplicationDeadline > DateTime.UtcNow);

            if (!string.IsNullOrWhiteSpace(department))
            {
                result = result.Where(j => string.Equals(j.Department, department, StringComparison.OrdinalIgnoreCase));
            }

            return Ok(result);
        }

        // GET api/careers/{jobId}
        [HttpGet("{jobId}")]
        public async Task<IActionResult> Get(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return BadRequest("jobId is required");
            var job = await _jobRepo.GetByJobIdAsync(jobId);
            if (job == null) return NotFound();
            return Ok(job);
        }

        // POST api/careers/{jobId}/apply
        // Body: JSON ApplicantApplyDto (resumeUrl required, portfolioUrl optional)
        [HttpPost("{jobId}/apply")]
        public async Task<IActionResult> Apply(string jobId, [FromBody] ApplicantApplyDto dto)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return BadRequest("jobId is required");
            if (dto == null) return BadRequest("application data required");

            var job = await _jobRepo.GetByJobIdAsync(jobId);
            if (job == null) return NotFound("Job not found");

            if (job.ApplicationDeadline < DateTime.UtcNow) return BadRequest("Application deadline passed");

            // Basic validations
            if (string.IsNullOrWhiteSpace(dto.FullName)) return BadRequest("FullName is required");
            if (string.IsNullOrWhiteSpace(dto.Email)) return BadRequest("Email is required");
            if (string.IsNullOrWhiteSpace(dto.ResumeUrl)) return BadRequest("ResumeUrl is required");

            // Validate URLs sent by frontend
            if (!Uri.IsWellFormedUriString(dto.ResumeUrl, UriKind.Absolute))
                return BadRequest("ResumeUrl is not a valid absolute URL");

            if (!string.IsNullOrWhiteSpace(dto.PortfolioUrl) &&
                !Uri.IsWellFormedUriString(dto.PortfolioUrl, UriKind.Absolute))
                return BadRequest("PortfolioUrl is not a valid absolute URL");

            // Prevent duplicate applications by same email for the same jobId
            // NOTE: If you expect many applicants you should add a repo method GetByJobIdAndEmailAsync for efficiency
            var existingForJob = await _appRepo.GetByJobIdAsync(jobId, 0, 100);
            var duplicate = existingForJob.FirstOrDefault(a =>
                string.Equals(a.Email?.Trim(), dto.Email.Trim(), StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                return Conflict(new { message = "An application with this email has already been submitted for this job." });
            }

            // Create applicant record (frontend provided urls)
            var applicant = new Applicant
            {
                JobObjectId = job.Id,
                JobId = job.JobId,
                JobTitle = job.JobTitle, // store jobTitle for filtering
                FullName = dto.FullName?.Trim(),
                Email = dto.Email?.Trim(),
                MobileNumber = dto.MobileNumber?.Trim(),
                CurrentCity = dto.CurrentCity?.Trim(),
                PositionAppliedFor = dto.PositionAppliedFor?.Trim(),
                RelevantExperienceYears = dto.RelevantExperienceYears,
                CurrentCTC = dto.CurrentCTC,
                ExpectedCTC = dto.ExpectedCTC,
                NoticePeriodInDays = dto.NoticePeriodInDays,
                ResumeUrl = dto.ResumeUrl?.Trim(),
                PortfolioUrl = dto.PortfolioUrl?.Trim(),
                WhyYobha = dto.WhyYobha?.Trim(),
                HowDidYouHear = dto.HowDidYouHear?.Trim(),
                AppliedAt = DateTime.UtcNow,
                Status = "Received"
            };

            var created = await _appRepo.CreateAsync(applicant);
            return CreatedAtAction(nameof(GetApplication), new { id = created.Id.ToString() }, created);
        }

        // GET api/careers/applications/{id}
        [HttpGet("applications/{id}")]
        public async Task<IActionResult> GetApplication(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");
            if (!ObjectId.TryParse(id, out var objId)) return BadRequest("invalid id");

            var app = await _appRepo.GetByIdAsync(objId);
            if (app == null) return NotFound();
            return Ok(app);
        }

        // ============================
        // Admin endpoints (require admin auth)
        // ============================

        /// <summary>
        /// Get applicants (admin) - filter by partial jobTitle (case-insensitive).
        /// Example: GET /api/careers/applicants?jobTitle=Frontend&page=0&limit=50
        /// </summary>
        [HttpGet("applicants")]
        [Authorize(Roles = "Admin")] // TODO: ensure role name matches your setup
        public async Task<IActionResult> GetApplicants([FromQuery] string jobTitle = null, [FromQuery] int page = 0, [FromQuery] int limit = 50)
        {
            if (page < 0) page = 0;
            if (limit <= 0 || limit > 500) limit = 50;
            var skip = page * limit;

            var list = await _appRepo.GetAllAsync(jobTitle, skip, limit);
            return Ok(list);
        }

        /// <summary>
        /// Update applicant status (admin).
        /// Example: PATCH /api/careers/applicants/{id}/status  body: { "status":"Shortlisted" }
        /// </summary>
        [HttpPatch("applicants/{id}/status")]
        [Authorize(Roles = "Admin")] // TODO: ensure role name matches your setup
        public async Task<IActionResult> UpdateApplicantStatus(string id, [FromBody] ApplicantStatusUpdateDto dto)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");
            if (!ObjectId.TryParse(id, out var objId)) return BadRequest("invalid id");
            if (dto == null || string.IsNullOrWhiteSpace(dto.Status)) return BadRequest("Status is required");

            // You may want to validate dto.Status against allowed values: Received|UnderReview|Rejected|Shortlisted|Hired
            await _appRepo.UpdateStatusAsync(objId, dto.Status.Trim());
            return NoContent();
        }
    }
}
