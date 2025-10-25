using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

public class Applicant
{
    [BsonId]
    public ObjectId Id { get; set; }


    [BsonElement("jobObjectId")]
    public ObjectId JobObjectId { get; set; }


    [BsonElement("jobId")]
    public string JobId { get; set; }


    // NEW: store job title at time of application so admin can filter by title
    [BsonElement("jobTitle")]
    public string JobTitle { get; set; }


    [BsonElement("fullName")]
    public string FullName { get; set; }


    [BsonElement("email")]
    public string Email { get; set; }


    [BsonElement("mobileNumber")]
    public string MobileNumber { get; set; }


    [BsonElement("currentCity")]
    public string CurrentCity { get; set; }


    [BsonElement("positionAppliedFor")]
    public string PositionAppliedFor { get; set; }


    [BsonElement("relevantExperienceYears")]
    public decimal RelevantExperienceYears { get; set; }


    [BsonElement("currentCTC")]
    public decimal? CurrentCTC { get; set; }


    [BsonElement("expectedCTC")]
    public decimal? ExpectedCTC { get; set; }


    [BsonElement("noticePeriodInDays")]
    public int? NoticePeriodInDays { get; set; }


    // frontend will handle storage and provide links
    [BsonElement("resumeUrl")]
    public string ResumeUrl { get; set; }


    [BsonElement("portfolioUrl")]
    public string PortfolioUrl { get; set; }


    [BsonElement("whyYobha")]
    public string WhyYobha { get; set; }


    [BsonElement("howDidYouHear")]
    public string HowDidYouHear { get; set; }


    [BsonElement("appliedAt")]
    public DateTime AppliedAt { get; set; }


    [BsonElement("status")]
    public string Status { get; set; } // Received | UnderReview | Rejected | Shortlisted | Hired


    [BsonElement("meta")]
    public object Meta { get; set; }
}