namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Phase mantiene la compatibilità con i client precedenti; la relazione persistente usa PhaseId.
public sealed record WorkActivity(string Id,string ProjectId,string Phase,string Title,string Assignee,DateOnly Start,DateOnly End,int PlannedMinutes,string State="Planned",int Progress=0,int Revision=0,string PhaseId="");
