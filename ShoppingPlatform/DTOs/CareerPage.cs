namespace ShoppingPlatform.DTOs
{
    public class ApplicantApplyDto
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string MobileNumber { get; set; }
        public string CurrentCity { get; set; }
        public string PositionAppliedFor { get; set; }
        public decimal RelevantExperienceYears { get; set; }
        public decimal? CurrentCTC { get; set; }
        public decimal? ExpectedCTC { get; set; }
        public int? NoticePeriodInDays { get; set; }
        public string ResumeUrl { get; set; }         // REQUIRED
        public string PortfolioUrl { get; set; }      // OPTIONAL
        public string WhyYobha { get; set; }
        public string HowDidYouHear { get; set; }
    }

    public class ApplicantStatusUpdateDto
    {
        /// <summary>
        /// New status to set for the applicant.
        /// Allowed values: "Received", "UnderReview", "Rejected", "Shortlisted", "Hired"
        /// </summary>
        public string Status { get; set; }
    }
    public class CreateJobPostingDto
    {
        /// <summary>
        /// User-entered unique Job ID (e.g., JD-001)
        /// </summary>
        public string JobId { get; set; }

        /// <summary>
        /// Title of the job position
        /// </summary>
        public string JobTitle { get; set; }

        /// <summary>
        /// Department name (e.g., Engineering, Marketing, HR)
        /// </summary>
        public string Department { get; set; }

        /// <summary>
        /// Job type (Full-Time | Part-Time | Internship | Contract)
        /// </summary>
        public string JobType { get; set; }

        /// <summary>
        /// Location details of the job
        /// </summary>
        public Location Location { get; set; }

        /// <summary>
        /// Salary range (Min, Max, Currency)
        /// </summary>
        public SalaryRange SalaryRange { get; set; }

        /// <summary>
        /// Required experience (e.g., 2-5 years)
        /// </summary>
        public ExperienceRange ExperienceRequired { get; set; }

        /// <summary>
        /// Minimum qualification required (e.g., B.Tech / MBA)
        /// </summary>
        public string Qualification { get; set; }

        /// <summary>
        /// List of required skills for the job
        /// </summary>
        public List<string> SkillsRequired { get; set; } = new();

        /// <summary>
        /// Brief description of the job role
        /// </summary>
        public string JobDescription { get; set; }

        /// <summary>
        /// List of key responsibilities
        /// </summary>
        public List<string> Responsibilities { get; set; } = new();

        /// <summary>
        /// Application fees for various categories
        /// </summary>
        public Dictionary<string, decimal> ApplicationFee { get; set; } = new();

        /// <summary>
        /// Last date to apply for the job
        /// </summary>
        public DateTime ApplicationDeadline { get; set; }

        /// <summary>
        /// Information about the admin posting the job
        /// </summary>
        public ContactInfo PostedBy { get; set; }

        /// <summary>
        /// Status of the job (Active | Closed | Draft)
        /// </summary>
        public string Status { get; set; }
    }

}
