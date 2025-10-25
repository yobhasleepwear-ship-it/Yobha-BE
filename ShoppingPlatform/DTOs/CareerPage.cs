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
}
