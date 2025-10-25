using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

public class JobPosting
{
    [BsonId]
    public ObjectId Id { get; set; }


    [BsonElement("jobId")]
    public string JobId { get; set; } // unique human readable ID provided by admin


    [BsonElement("jobTitle")]
    public string JobTitle { get; set; }


    [BsonElement("department")]
    public string Department { get; set; }


    [BsonElement("jobType")]
    public string JobType { get; set; }


    [BsonElement("location")]
    public Location Location { get; set; }


    [BsonElement("salaryRange")]
    public SalaryRange SalaryRange { get; set; }


    [BsonElement("experienceRequired")]
    public ExperienceRange ExperienceRequired { get; set; }


    [BsonElement("qualification")]
    public string Qualification { get; set; }


    [BsonElement("skillsRequired")]
    public List<string> SkillsRequired { get; set; } = new();


    [BsonElement("jobDescription")]
    public string JobDescription { get; set; }


    [BsonElement("responsibilities")]
    public List<string> Responsibilities { get; set; } = new();


    [BsonElement("applicationFee")]
    public Dictionary<string, decimal> ApplicationFee { get; set; } = new();


    [BsonElement("applicationDeadline")]
    public DateTime ApplicationDeadline { get; set; }


    [BsonElement("postedBy")]
    public ContactInfo PostedBy { get; set; }


    [BsonElement("status")]
    public string Status { get; set; } // Active | Closed | Draft


    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }


    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}


public class Location
{
    public string City { get; set; }
    public string State { get; set; }
    public string Country { get; set; }
    public bool Remote { get; set; }
}


public class SalaryRange
{
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public string Currency { get; set; }
}


public class ExperienceRange
{
    public int Min { get; set; }
    public int Max { get; set; }
    public string Unit { get; set; } = "years";
}


public class ContactInfo
{
    public string Name { get; set; }
    public string Email { get; set; }
}