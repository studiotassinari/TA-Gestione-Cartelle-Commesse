namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record CustomerSource(string Provider,string ExternalId,string Name,string Vat,string TaxCode,DateTime ReadUtc);
public sealed record Customer(string Id,string Name,string Vat="",string TaxCode="",string Address="",string Email="",int Revision=0,CustomerSource[]? Sources=null);
public sealed record WorkProject(string Id,string Code,string Title,string CustomerId="",string HoursMode="Blocked",string Evidence="",string Reason="",DateOnly? Until=null,int Revision=0);
public sealed record WorkActivity(string Id,string ProjectId,string Phase,string Title,string Assignee,DateOnly Start,DateOnly End,int PlannedMinutes,string State="Planned",int Progress=0,int Revision=0);
public sealed record WorkTime(string Id,string ProjectId,string ActivityId,DateOnly Day,int Minutes,string Description,string User="",int Revision=0);
public sealed record BusinessQuery(DateOnly From,DateOnly To);
public sealed record BusinessCommand(string Action,BusinessQuery? Query=null,Customer? Customer=null,WorkProject? Project=null,WorkActivity? Activity=null,WorkTime? Time=null,string? Id=null,int Revision=0,string? Csv=null);
public sealed record BusinessSnapshot(string User,bool CanManage,Customer[] Customers,WorkProject[] Projects,WorkActivity[] Activities,WorkTime[] Times,string[] Collaborators,string BackupStatus);
public sealed record CustomerImportResult(int Read,int Added,int Linked,string[] Issues);
