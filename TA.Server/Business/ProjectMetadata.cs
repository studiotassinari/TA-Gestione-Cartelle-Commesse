using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA.Server.Business;

public static class ProjectMetadata
{
    public static WorkProject FromFolder(FolderRecord row,string category="C")
    {
        var project=Normalize(new(row.Id,row.Code,row.Client+" — "+row.Subject,Subject:row.Subject,SourceCustomerName:row.Client,LastActivityUtc:row.Inspection?.LastFileWriteUtc??row.ArchiveModifiedUtc,Category:category));
        // Il nome della cartella anno prevale sull'inferenza dal codice a due cifre.
        foreach(var segment in row.Path.Split('\\','/'))
            if(Regex.IsMatch(segment,@"^(19|20)\d{2}$")&&int.TryParse(segment,out var year)&&year%100==project.Year%100)
                return project with{Year=year};
        return project;
    }
    public static WorkProject Normalize(WorkProject project)
    {
        var match=Regex.Match(project.Code,@"^[CG](?<year>\d{2})(?<number>\d{3})$");
        return project with {
            Year=project.Year!=0?project.Year:match.Success?2000+int.Parse(match.Groups["year"].Value):0,
            Number=project.Number!=0?project.Number:match.Success?int.Parse(match.Groups["number"].Value):0,
            Subject=string.IsNullOrWhiteSpace(project.Subject)?project.Title.Split(" — ",2).Last():project.Subject
        };
    }
}
