using DwgTrueView.Core;

namespace DwgTrueView.Tests;

public sealed class WorkspaceTabCollectionTests
{
    [Fact]
    public void OpeningADrawingAddsANewTabInsteadOfReplacingTheActiveOne()
    {
        var workspace = new WorkspaceTabCollection();
        PackedCadDrawing first = Drawing("first.dxf");
        PackedCadDrawing second = Drawing("second.dxf");

        DrawingWorkspace tabA = workspace.Add(first);
        DrawingWorkspace tabB = workspace.Add(second);

        Assert.Equal(2, workspace.Count);
        Assert.Same(first, tabA.Drawing);
        Assert.Same(second, tabB.Drawing);
        Assert.Same(tabB, workspace.Active);
        Assert.Contains(tabA, workspace.Tabs);
    }

    [Fact]
    public void SwitchingTabsKeepsTheSameParsedDrawingCameraAndLayerState()
    {
        var workspace = new WorkspaceTabCollection();
        DrawingWorkspace first = workspace.Add(Drawing("a.dxf", layerCount: 2));
        DrawingWorkspace second = workspace.Add(Drawing("b.dxf", layerCount: 1));
        first.Camera.Fit(
            new CadBounds2(new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(10, 10)),
            new System.Numerics.Vector2(200, 100),
            margin: 0);
        first.LayerVisibility[0] = false;

        Assert.True(workspace.Activate(first.Id));
        DrawingWorkspace active = Assert.IsType<DrawingWorkspace>(workspace.Active);
        Assert.Same(first, active);
        Assert.Same(first.Drawing, active.Drawing);
        Assert.Same(first.Camera, active.Camera);
        Assert.Same(first.LayerVisibility, active.LayerVisibility);
        Assert.False(active.LayerVisibility[0]);
        Assert.Equal(first.Camera.Center, active.Camera.Center);
        Assert.Same(second.Drawing, workspace.Find(second.Id)!.Drawing);
    }

    [Fact]
    public void ClosingTheActiveTabSelectsTheNeighborAndDropsOnlyThatDrawing()
    {
        var workspace = new WorkspaceTabCollection();
        DrawingWorkspace first = workspace.Add(Drawing("a.dxf"));
        DrawingWorkspace middle = workspace.Add(Drawing("b.dxf"));
        DrawingWorkspace last = workspace.Add(Drawing("c.dxf"));

        Assert.True(workspace.Close(middle.Id));
        Assert.Equal(2, workspace.Count);
        Assert.Same(last, workspace.Active);
        Assert.Same(first.Drawing, workspace.Find(first.Id)!.Drawing);
        Assert.Null(workspace.Find(middle.Id));

        Assert.True(workspace.Close(last.Id));
        Assert.Same(first, workspace.Active);

        Assert.True(workspace.Close(first.Id));
        Assert.Equal(0, workspace.Count);
        Assert.Null(workspace.Active);
    }

    [Fact]
    public void MoveReordersTabsWithoutChangingTheActiveDrawing()
    {
        var workspace = new WorkspaceTabCollection();
        DrawingWorkspace first = workspace.Add(Drawing("a.dxf"));
        DrawingWorkspace second = workspace.Add(Drawing("b.dxf"));
        DrawingWorkspace third = workspace.Add(Drawing("c.dxf"));

        Assert.True(workspace.Move(third.Id, 0));
        Assert.Equal(
            new[] { third.Id, first.Id, second.Id },
            workspace.Tabs.Select(tab => tab.Id));
        Assert.Same(third, workspace.Active);

        Assert.True(workspace.Move(first.Id, 2));
        Assert.Equal(
            new[] { third.Id, second.Id, first.Id },
            workspace.Tabs.Select(tab => tab.Id));
        Assert.Same(third, workspace.Active);
        Assert.False(workspace.Move(first.Id, 2));
    }

    private static PackedCadDrawing Drawing(string fileName, int layerCount = 1)
    {
        var layers = Enumerable.Range(0, layerCount)
            .Select(index => new CadLayer
            {
                Id = index,
                Name = index == 0 ? "0" : $"L{index}",
                ColorArgb = unchecked((int)0xFFFFFFFF),
                IsInitiallyVisible = true,
            })
            .ToArray();
        return new PackedCadDrawing(
            Path.Combine("C:", "drawings", fileName),
            [],
            layers,
            [],
            new CadBounds2(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One),
            1,
            0,
            0);
    }
}
