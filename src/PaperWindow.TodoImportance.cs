using System.Windows;
using System.Windows.Controls;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private void AddTodoImportanceMenu(ContextMenu menu, string[] ids)
    {
        var selected = _paper.Items.Where(item => ids.Contains(item.Id)).ToList();
        var priorities = new MenuItem { Header = Strings.Get("TodoPriorityMenu") };
        foreach (var (key,value) in new[] { ("TodoPriorityNormal",0),("TodoPriorityMedium",1),("TodoPriorityHigh",2) }) {
            var option = MenuItem(Strings.Get(key), (_,_) => SetTodoImportance(ids,value,null));
            option.IsCheckable = true; option.IsChecked = selected.Count > 0 && selected.All(item => item.Priority == value);
            priorities.Items.Add(option);
        }
        menu.Items.Add(priorities);
        var urgent = MenuItem(Strings.Get("TodoUrgent"), (_,_) => {});
        urgent.IsCheckable = true; urgent.IsChecked = selected.Count > 0 && selected.All(item => item.IsUrgent);
        urgent.Click += (_,_) => SetTodoImportance(ids,null,urgent.IsChecked);
        menu.Items.Add(urgent);
        menu.Items.Add(MenuItem(Strings.Get("TodoSortByPriority"), (_,_) => SortTodosByImportance()));
        menu.Items.Add(MenuSeparator());
    }

    private void SetTodoImportance(string[] ids, int? priority, bool? urgent)
    {
        if (priority is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(priority));
        var selected = _paper.Items.Where(item => ids.Contains(item.Id) &&
            (priority.HasValue && item.Priority != priority.Value || urgent.HasValue && item.IsUrgent != urgent.Value)).ToList();
        if (selected.Count == 0) return;
        var focused = CurrentFocusedTodoItemId(); PushUndoSnapshot();
        foreach (var item in selected) { if (priority.HasValue) item.Priority = priority.Value; if (urgent.HasValue) item.IsUrgent = urgent.Value; }
        _controller.MarkDirty(); ReconcileTodoRows(selected.Select(item => item.Id),focused);
    }

    private void SortTodosByImportance()
    {
        var ordered = _paper.Items.OrderBy(TodoRules.IsPlaceholder).ThenBy(item => item.Done)
            .ThenByDescending(item => item.IsUrgent).ThenByDescending(item => item.Priority).ThenBy(item => item.Order).ToList();
        if (ordered.Select(item => item.Id).SequenceEqual(_paper.Items.Select(item => item.Id))) return;
        var focused = CurrentFocusedTodoItemId(); PushUndoSnapshot();
        _paper.Items = ordered; NormalizeOrders(); _controller.MarkDirty(); ReconcileTodoRows(focusItemId:focused);
    }
}
