using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PaperTodo;
using PaperTodo.Plugin;

internal static class TodoPriorityChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task Run()
    {
        void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
        var legacy = JsonSerializer.Deserialize<PaperItem>("{\"Text\":\"old task\"}")!;
        Require(legacy.Priority == 0 && !legacy.IsUrgent,"Legacy items should have no priority marks");
        var state = new AppState { TelemetryEnabled=false,EnableAnimations=false,UseCapsuleMode=false,UseDeepCapsuleMode=false,
            UsePersistentPowerShellProcess=false,McpEnabled=false,AutoMoveCompletedTodosToBottom=false,AutoClearCompletedTodos=false };
        state.Papers.Add(new PaperData { Id="priority",Type=PaperTypes.Todo,IsVisible=true,Width=440,Height=360,
            Items=[new() { Id="normal",Text="Normal",Order=0 },new() { Id="high",Text="High",Order=1 },new() { Id="urgent",Text="Urgent",Order=2 }] });
        var store = new StateStore(); store.SaveJsonSync(store.SerializeState(state),1);
        using var controller = new AppController(); await controller.StartAsync(createDefaultPaper:false);
        await Dispatcher.CurrentDispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var paper=controller.State.Papers.Single(p=>p.Id=="priority");
        var windows=(Dictionary<string,PaperWindow>)typeof(AppController).GetField("_windows",Private)!.GetValue(controller)!;
        var window=windows[paper.Id];
        void Invoke(string name,params object?[] args) => typeof(PaperWindow).GetMethod(name,Private)!.Invoke(window,args);
        var editors=(Dictionary<string,TodoTextBox>)typeof(PaperWindow).GetField("_todoEditors",Private)!.GetValue(window)!;
        var taskEditor=editors["normal"];
        var opening=(ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] { taskEditor,true,0d,0d },null)!;
        taskEditor.RaiseEvent(opening);
        Require(taskEditor.ContextMenu!.Items.OfType<MenuItem>().Any(m=>m.Header as string == Strings.Get("TodoPriorityMenu")),"Actual task context-menu opening omitted priority");
        Invoke("SetTodoImportance",new string[] { "high" },2,null);
        Invoke("SetTodoImportance",new string[] { "urgent" },null,true);
        Require(paper.Items[1].Priority==2 && paper.Items[2].IsUrgent,"Priority and urgency must be independent");
        Require(paper.Items.Select(i=>i.Id).SequenceEqual(new[] {"normal","high","urgent"}),"Marking must not reorder existing tasks");
        Require(Find(window).OfType<TextBlock>().Any(t=>t.Text.Contains("★")) && Find(window).OfType<TextBlock>().Any(t=>t.Text.Contains("！")),"Special marks must be visible");
        var rows=(StackPanel)typeof(PaperWindow).GetField("_todoPanel",Private)!.GetValue(window)!;
        var highRow=rows.Children.OfType<Border>().Single(r=>r.Tag as string=="high");
        Require(highRow.BorderThickness.Left>=3 && Find(highRow).OfType<TextBlock>().Any(t=>t.Text.Contains(Strings.Get("TodoPriorityHigh"))),"High priority should have an accent line and a full visible label");
        var urgentRow=rows.Children.OfType<Border>().Single(r=>r.Tag as string=="urgent");
        Require(urgentRow.BorderThickness.Left>=3 && Find(urgentRow).OfType<TextBlock>().Any(t=>t.Text.Contains(Strings.Get("TodoUrgent"))),"Urgency should have an accent line and full visible label");
        Invoke("Undo"); Require(!paper.Items.Single(i=>i.Id=="urgent").IsUrgent,"Urgency undo failed");
        Invoke("Redo"); Require(paper.Items.Single(i=>i.Id=="urgent").IsUrgent,"Urgency redo failed");
        Invoke("SortTodosByImportance"); Require(paper.Items.Select(i=>i.Id).SequenceEqual(new[] {"urgent","high","normal"}),"Sort must prefer urgent then high");
        Invoke("Undo"); Require(paper.Items.Select(i=>i.Id).SequenceEqual(new[] {"normal","high","urgent"}),"Sorting should undo in one operation");
        Invoke("SetTodoImportance",new string[] { "normal","high" },1,true);
        Require(paper.Items.Where(i=>i.Id!="urgent").All(i=>i.Priority==1 && i.IsUrgent),"Batch marking failed");
        Invoke("Undo"); Require(paper.Items.Single(i=>i.Id=="normal").Priority==0 && paper.Items.Single(i=>i.Id=="high").Priority==2,"Batch undo failed");
        var menu=new ContextMenu(); Invoke("AddTodoImportanceMenu",menu,new string[] { "normal" });
        var priorityMenu=menu.Items.OfType<MenuItem>().Single(m=>m.Header as string == Strings.Get("TodoPriorityMenu"));
        var medium=priorityMenu.Items.OfType<MenuItem>().Single(m=>m.Header as string == Strings.Get("TodoPriorityMedium"));
        medium.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(paper.Items.Single(i=>i.Id=="normal").Priority==1,"Native priority menu failed");
        var mediumRow=((StackPanel)typeof(PaperWindow).GetField("_todoPanel",Private)!.GetValue(window)!).Children.OfType<Border>().Single(r=>r.Tag as string=="normal");
        Require(mediumRow.BorderThickness.Left>=2 && mediumRow.BorderThickness.Left<3 && Find(mediumRow).OfType<TextBlock>().Any(t=>t.Text.Contains(Strings.Get("TodoPriorityMedium"))),"Medium priority needs a full label and a lighter accent");
        Invoke("Undo");
        var urgentMenu=menu.Items.OfType<MenuItem>().Single(m=>m.Header as string == Strings.Get("TodoUrgent"));
        urgentMenu.IsChecked=true; urgentMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(paper.Items.Single(i=>i.Id=="normal").IsUrgent,"Native urgency menu failed"); Invoke("Undo");
        controller.State.Theme="dark"; Theme.Invalidate(); window.UpdateTheme();
        Require(Find(window).OfType<TextBlock>().Where(t=>t.Text.Contains("★")).All(t=>t.Foreground == Theme.ActiveBrush),"Priority badge did not refresh theme");
        controller.State.Theme="light"; Theme.Invalidate(); window.UpdateTheme();
        var previewContext=new EdgeCapsulePreviewContext(paper,()=>paper.Title,false,()=>"",()=>"off",(_,_)=>false,_=>false,()=>new Style(),()=>"",_=>{},new());
        var descriptor=TodoEdgeCapsulePreviewProvider.Instance.Describe(previewContext);
        var preview=(EdgeCapsuleLivePreviewView)descriptor.CreateContent(descriptor.Size); preview.PrepareForFirstDisplay();
        Require(Find(preview).OfType<TextBlock>().Any(t=>t.Text.Contains("★")) && Find(preview).OfType<TextBlock>().Any(t=>t.Text.Contains("！")),"Preview lost priority/urgency marks");
        controller.PaperCommands.UpdateTodo(new UpdateTodoRequest { PaperId=paper.Id,TodoId="normal",Text="External change" },PaperOperationContext.Mcp());
        Invoke("Undo");
        Require(paper.Items.Single(i=>i.Id=="normal").Text=="Normal" && paper.Items.Single(i=>i.Id=="high").Priority==2 && paper.Items.Single(i=>i.Id=="urgent").IsUrgent,"External mutation undo cleared priority marks");
        var copied=TodoRules.Clone(paper.Items.Single(i=>i.Id=="high")); Require(copied.Priority==2,"History/move cloning lost priority");
        var blank=new PaperItem { Priority=2 }; Require(TodoRules.HasMeaningfulContent(blank),"A marked item must not be discarded as an empty placeholder");
        var currentRows=(StackPanel)typeof(PaperWindow).GetField("_todoPanel",Private)!.GetValue(window)!;
        var currentHigh=currentRows.Children.OfType<Border>().Single(r=>r.Tag as string=="high");
        var complete=Find(currentHigh).OfType<CheckBox>().First(); complete.IsChecked=true;
        Require(currentHigh.BorderThickness.Left==0,"Completing a task should clear its strong accent immediately");
        complete.IsChecked=false;
        Require(currentHigh.BorderThickness.Left>=3,"Unchecking a task should restore the priority accent");
        store.SaveJsonSync(store.SerializeState(controller.State),2);
        var restored=new StateStore().Load().Papers.Single(p=>p.Id=="priority");
        Require(restored.Items.Single(i=>i.Id=="high").Priority==2 && restored.Items.Single(i=>i.Id=="urgent").IsUrgent,"StateStore lost marks");
        Console.WriteLine("PASS todo priority/urgency: legacy, native badges, stable order, batch edit, undo/redo, explicit sorting, clone and persistence");
    }
    private static IEnumerable<DependencyObject> Find(DependencyObject root) {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach(var nested in Find(child)) yield return nested;
    }
}
