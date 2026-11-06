using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Controllo WPF senza dipendenze commerciali: aggrega per commessa o collaboratore.
public sealed class GanttView : Canvas
{
    public void Update(WorkActivity[] rows, DateOnly from, DateOnly to, bool byPerson, Func<string,string> code, Action<WorkActivity> select)
    {
        Children.Clear(); if (to < from) return;
        var days = to.DayNumber - from.DayNumber + 1;
        double scale = days <= 45 ? 24 : days <= 100 ? 12 : 5;
        double left = 280, y = 38; Width = left + days * scale + 20;
        for (int day = 0; day < days; day += days <= 45 ? 2 : days <= 100 ? 7 : 14)
        {
            var label = new TextBlock { Text = from.AddDays(day).ToString("dd/MM"), FontSize = 10 };
            SetLeft(label, left + day * scale); SetTop(label, 8); Children.Add(label);
        }
        foreach (var group in rows.GroupBy(a => byPerson ? a.Assignee : code(a.ProjectId)).OrderBy(g => g.Key))
        {
            var title = new TextBlock { Text = $"{group.Key} · {group.Sum(a => (long)a.PlannedMinutes) / 60m:0.#} h previste", FontWeight = FontWeights.Bold, Width = left - 10, TextTrimming = TextTrimming.CharacterEllipsis };
            SetTop(title, y); Children.Add(title); y += 30;
            foreach (var a in group.OrderBy(a => a.Start))
            {
                var label = new TextBlock { Text = a.Title, Width = left - 18, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = a.Title };
                SetTop(label, y + 4); Children.Add(label);
                int start = Math.Max(0, a.Start.DayNumber - from.DayNumber), end = Math.Min(days, a.End.DayNumber - from.DayNumber + 1);
                var bar = new Button { Width = Math.Max(4, (end - start) * scale), Height = 26, Padding = new Thickness(2), Margin = new Thickness(0), Content = a.Progress + "%", Background = new SolidColorBrush(a.State == "Done" ? Colors.SlateGray : Color.FromRgb(19,95,110)), ToolTip = $"{code(a.ProjectId)} · {a.Phase}\n{a.Title}\n{a.Assignee}\n{a.Start} — {a.End}\n{a.PlannedMinutes / 60m:0.#} ore previste" };
                bar.Click += (_, _) => select(a); SetLeft(bar, left + start * scale); SetTop(bar, y); Children.Add(bar); y += 34;
            }
            y += 10;
        }
        Height = Math.Max(120, y);
    }
}
