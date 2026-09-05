using DwgTrueView.Core;

namespace DwgTrueView.Tests;

public sealed class UndoStackTests
{
    [Fact]
    public void UndoReversesActionsLastInFirstOut()
    {
        var stack = new UndoStack();
        var log = new List<string>();
        stack.Push(new DelegateUndoAction("first", () => log.Add("u1"), () => log.Add("r1")));
        stack.Push(new DelegateUndoAction("second", () => log.Add("u2"), () => log.Add("r2")));

        Assert.True(stack.CanUndo);
        Assert.Equal("second", stack.NextName);
        Assert.True(stack.TryUndo());
        Assert.Equal("first", stack.NextName);
        Assert.True(stack.TryUndo());
        Assert.False(stack.CanUndo);
        Assert.False(stack.TryUndo());
        Assert.Equal(["u2", "u1"], log);
    }

    [Fact]
    public void RedoReappliesTheUndoneAction()
    {
        var stack = new UndoStack();
        int value = 0;
        stack.Push(new DelegateUndoAction("step", () => value = 0, () => value = 1));
        value = 1;

        Assert.True(stack.TryUndo());
        Assert.Equal(0, value);
        Assert.True(stack.CanRedo);
        Assert.Equal("step", stack.NextRedoName);
        Assert.True(stack.TryRedo());
        Assert.Equal(1, value);
        Assert.False(stack.CanRedo);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void PushAfterUndoClearsRedo()
    {
        var stack = new UndoStack();
        stack.Push(new DelegateUndoAction("a", () => { }, () => { }));
        stack.TryUndo();
        Assert.True(stack.CanRedo);
        stack.Push(new DelegateUndoAction("b", () => { }, () => { }));
        Assert.False(stack.CanRedo);
        Assert.Equal("b", stack.NextName);
    }

    [Fact]
    public void PushDropsTheOldestEntryPastTheCap()
    {
        var stack = new UndoStack();
        for (int i = 0; i < UndoStack.MaximumEntries + 5; i++)
        {
            string name = i.ToString();
            stack.Push(new DelegateUndoAction(name, () => { }, () => { }));
        }

        Assert.Equal(UndoStack.MaximumEntries, stack.Count);
        Assert.Equal((UndoStack.MaximumEntries + 4).ToString(), stack.NextName);
    }
}
