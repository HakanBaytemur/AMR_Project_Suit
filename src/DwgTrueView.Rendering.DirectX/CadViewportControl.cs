using System.Numerics;
using System.Runtime.InteropServices;
using DwgTrueView.Core;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using DataBox = SharpDX.DataBox;
using Device = SharpDX.Direct3D11.Device;
using RawColor4 = SharpDX.Mathematics.Interop.RawColor4;
using SharpDXException = SharpDX.SharpDXException;
using Utilities = SharpDX.Utilities;

namespace DwgTrueView.Rendering.DirectX;

public sealed class WorldCursorEventArgs(Vector2 world) : EventArgs
{
    public Vector2 World { get; } = world;
}

public sealed class CameraUndoEventArgs(
    ViewCamera2D camera,
    Vector2 previousCenter,
    float previousUnitsPerPixel) : EventArgs
{
    public ViewCamera2D Camera { get; } = camera;
    public Vector2 PreviousCenter { get; } = previousCenter;
    public float PreviousUnitsPerPixel { get; } = previousUnitsPerPixel;
}

/// <summary>
/// A display-only Direct3D 11 CAD viewport. CAD entities occupy one immutable
/// GPU line-list vertex buffer; layer toggles select draw ranges without
/// rebuilding or copying geometry.
/// </summary>
public sealed class CadViewportControl : Control
{
    private const int MaximumGridVertices = ViewportOverlay.MaxVertices;
    private const string ShaderSource = """
        cbuffer CameraBuffer : register(b0)
        {
            float4 View;
        };

        struct VertexInput
        {
            float2 Position : POSITION;
            float4 Color : COLOR0;
        };

        struct VertexOutput
        {
            float4 Position : SV_POSITION;
            float4 Color : COLOR0;
        };

        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            float2 projected = (input.Position - View.xy) / View.zw;
            output.Position = float4(projected.x, projected.y, 0.0f, 1.0f);
            output.Color = input.Color;
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_TARGET
        {
            return input.Color;
        }
        """;

    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly CadVertex[] _gridVertices = new CadVertex[MaximumGridVertices];
    private Device? _device;
    private DeviceContext? _context;
    private SwapChain? _swapChain;
    private RenderTargetView? _renderTarget;
    private VertexShader? _vertexShader;
    private PixelShader? _pixelShader;
    private InputLayout? _inputLayout;
    private RasterizerState? _rasterizer;
    private Buffer? _cameraBuffer;
    private Buffer? _cadBuffer;
    private Buffer? _fillBuffer;
    private Buffer? _gridBuffer;
    private PackedCadDrawing? _drawing;
    private bool[] _layerVisibility = [];
    private bool _dirty = true;
    private bool _gridDirty = true;
    private bool _panning;
    private Point _lastMouse;
    private Vector2 _panCenter;
    private float _panUnitsPerPixel;
    private bool _didPan;
    private readonly System.Windows.Forms.Timer _wheelUndoTimer;
    private Vector2 _wheelCenter;
    private float _wheelUnitsPerPixel;
    private bool _wheelUndoArmed;
    private int _gridVertexCount;
    private int _accentVertexStart;
    private int _accentVertexCount;
    private bool _disposed;
    private bool _gridVisible = true;
    private bool _zoomWindowArmed;
    private bool _zoomDragging;
    private Point _zoomStart;
    private Point _zoomCurrent;
    private Buffer? _zoomBuffer;
    private readonly CadVertex[] _zoomVertices = new CadVertex[8];
    private static readonly uint ZoomWindowColor = CadVertex.FromArgb(unchecked((int)0xFF33D6FF));

    public CadViewportControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.Opaque
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        TabStop = true;
        DoubleBuffered = false;
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += (_, _) =>
        {
            if (_dirty && IsHandleCreated && Visible)
            {
                RenderFrame();
            }
        };
        _renderTimer.Start();
        _wheelUndoTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _wheelUndoTimer.Tick += (_, _) => FlushWheelUndo();
    }

    public ViewCamera2D Camera { get; private set; } = new();
    public PackedCadDrawing? Drawing => _drawing;

    public bool GridVisible
    {
        get => _gridVisible;
        set
        {
            if (_gridVisible == value)
            {
                return;
            }
            _gridVisible = value;
            _gridDirty = true;
            RequestFrame();
        }
    }

    public event EventHandler<WorldCursorEventArgs>? WorldCursorChanged;
    public event Action<Exception>? RenderFailed;
    public event Action<bool>? ZoomWindowArmedChanged;
    public event EventHandler<CameraUndoEventArgs>? CameraUndoCommitted;

    public bool ZoomWindowArmed => _zoomWindowArmed;

    public void LoadDrawing(PackedCadDrawing? drawing)
    {
        PresentSession(
            drawing,
            new ViewCamera2D(),
            drawing?.Layers.Span
                .ToArray()
                .Select(static layer => layer.IsInitiallyVisible)
                .ToArray()
                ?? [],
            fitExtents: drawing is not null);
    }

    /// <summary>
    /// Swap the on-screen session without re-parsing. GPU buffers are rebuilt
    /// from the already-packed drawing; camera and layer flags are the tab's.
    /// </summary>
    public void PresentSession(
        PackedCadDrawing? drawing,
        ViewCamera2D camera,
        bool[] layerVisibility,
        bool fitExtents)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(layerVisibility);
        FlushWheelUndo();
        Camera = camera;
        _drawing = drawing;
        _layerVisibility = layerVisibility;
        UploadCadBuffer();
        if (fitExtents)
        {
            ZoomExtents(recordUndo: false);
            return;
        }
        _gridDirty = true;
        RequestFrame();
    }

    public void SetLayerVisible(int layerId, bool visible)
    {
        if ((uint)layerId >= (uint)_layerVisibility.Length)
        {
            return;
        }
        _layerVisibility[layerId] = visible;
        RequestFrame();
    }

    public bool IsLayerVisible(int layerId) =>
        (uint)layerId < (uint)_layerVisibility.Length
        && _layerVisibility[layerId];

    public void ZoomExtents(bool recordUndo = true)
    {
        FlushWheelUndo();
        Vector2 center = Camera.Center;
        float unitsPerPixel = Camera.UnitsPerPixel;
        if (_drawing is not null)
        {
            Camera.Fit(
                _drawing.Bounds,
                new Vector2(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)));
        }
        _gridDirty = true;
        RequestFrame();
        if (recordUndo)
        {
            CommitCameraUndo(center, unitsPerPixel);
        }
    }

    public void RefreshView()
    {
        _gridDirty = true;
        RequestFrame();
    }

    public void CommitPendingCameraUndo() => FlushWheelUndo();

    public void BeginZoomWindow()
    {
        _zoomWindowArmed = true;
        Cursor = Cursors.Cross;
        ZoomWindowArmedChanged?.Invoke(true);
        RequestFrame();
    }

    public void CancelZoomWindow()
    {
        bool wasArmed = _zoomWindowArmed || _zoomDragging;
        _zoomWindowArmed = false;
        _zoomDragging = false;
        if (Capture)
        {
            Capture = false;
        }
        Cursor = Cursors.Cross;
        RequestFrame();
        if (wasArmed)
        {
            ZoomWindowArmedChanged?.Invoke(false);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode)
        {
            CreateDeviceResources();
            UploadCadBuffer();
            RequestFrame();
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        DisposeDeviceResources();
        base.OnHandleDestroyed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_swapChain is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }
        try
        {
            CreateRenderTarget(resizeBuffers: true);
            _gridDirty = true;
            RequestFrame();
        }
        catch (Exception exception)
        {
            RenderFailed?.Invoke(exception);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!DesignMode)
        {
            RenderFrame();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Right && (_zoomWindowArmed || _zoomDragging))
        {
            CancelZoomWindow();
            return;
        }
        if (e.Button == MouseButtons.Left && _zoomWindowArmed)
        {
            _zoomDragging = true;
            _zoomStart = e.Location;
            _zoomCurrent = e.Location;
            Capture = true;
            RequestFrame();
            return;
        }
        if (e.Button == MouseButtons.Middle)
        {
            FlushWheelUndo();
            _panning = true;
            _didPan = false;
            _panCenter = Camera.Center;
            _panUnitsPerPixel = Camera.UnitsPerPixel;
            _lastMouse = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _zoomDragging)
        {
            Point end = e.Location;
            _zoomDragging = false;
            Capture = false;
            ApplyZoomWindow(_zoomStart, end);
            CancelZoomWindow();
            return;
        }
        if (e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Capture = false;
            Cursor = Cursors.Cross;
            if (_didPan)
            {
                CommitCameraUndo(_panCenter, _panUnitsPerPixel);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_zoomDragging)
        {
            _zoomCurrent = e.Location;
            RequestFrame();
        }
        else if (_panning)
        {
            Point delta = new(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
            _lastMouse = e.Location;
            Camera.PanPixels(new Vector2(delta.X, delta.Y));
            _didPan = true;
            _gridDirty = true;
            RequestFrame();
        }
        WorldCursorChanged?.Invoke(
            this,
            new WorldCursorEventArgs(
                Camera.ScreenToWorld(
                    new Vector2(e.X, e.Y),
                    new Vector2(
                        Math.Max(1, ClientSize.Width),
                        Math.Max(1, ClientSize.Height)))));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_wheelUndoArmed)
        {
            _wheelCenter = Camera.Center;
            _wheelUnitsPerPixel = Camera.UnitsPerPixel;
            _wheelUndoArmed = true;
        }
        float factor = MathF.Pow(0.85f, e.Delta / 120f);
        Camera.ZoomAt(
            new Vector2(e.X, e.Y),
            new Vector2(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)),
            factor);
        _gridDirty = true;
        RequestFrame();
        _wheelUndoTimer.Stop();
        _wheelUndoTimer.Start();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Middle)
        {
            ZoomExtents();
        }
    }

    protected override bool ProcessCmdKey(
        ref System.Windows.Forms.Message msg,
        Keys keyData)
    {
        if (keyData is Keys.F or Keys.Home)
        {
            ZoomExtents();
            return true;
        }
        if (keyData is Keys.Escape && (_zoomWindowArmed || _zoomDragging))
        {
            CancelZoomWindow();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _wheelUndoTimer.Stop();
            _wheelUndoTimer.Dispose();
            _renderTimer.Stop();
            _renderTimer.Dispose();
            DisposeDeviceResources();
        }
        base.Dispose(disposing);
    }

    private void CreateDeviceResources()
    {
        DisposeDeviceResources();
        var description = new SwapChainDescription
        {
            BufferCount = 2,
            Flags = SwapChainFlags.None,
            IsWindowed = true,
            ModeDescription = new ModeDescription(
                Math.Max(1, ClientSize.Width),
                Math.Max(1, ClientSize.Height),
                new Rational(60, 1),
                Format.R8G8B8A8_UNorm),
            OutputHandle = Handle,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.Discard,
            Usage = Usage.RenderTargetOutput,
        };
        try
        {
            Device.CreateWithSwapChain(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                description,
                out _device,
                out _swapChain);
        }
        catch (SharpDXException)
        {
            Device.CreateWithSwapChain(
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                description,
                out _device,
                out _swapChain);
        }

        _context = _device.ImmediateContext;
        using Factory factory = _swapChain.GetParent<Factory>();
        factory.MakeWindowAssociation(Handle, WindowAssociationFlags.IgnoreAltEnter);
        CreateShaders();
        _cameraBuffer = new Buffer(
            _device,
            Utilities.SizeOf<Vector4>(),
            ResourceUsage.Default,
            BindFlags.ConstantBuffer,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);
        _gridBuffer = new Buffer(
            _device,
            new BufferDescription(
                MaximumGridVertices * CadVertex.SizeInBytes,
                ResourceUsage.Dynamic,
                BindFlags.VertexBuffer,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0));
        _zoomBuffer = new Buffer(
            _device,
            new BufferDescription(
                _zoomVertices.Length * CadVertex.SizeInBytes,
                ResourceUsage.Dynamic,
                BindFlags.VertexBuffer,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0));
        CreateRenderTarget(resizeBuffers: false);
        _gridDirty = true;
    }

    private void CreateShaders()
    {
        using CompilationResult vertexCode = ShaderBytecode.Compile(
            ShaderSource,
            "VSMain",
            "vs_4_0",
            ShaderFlags.OptimizationLevel3);
        using CompilationResult pixelCode = ShaderBytecode.Compile(
            ShaderSource,
            "PSMain",
            "ps_4_0",
            ShaderFlags.OptimizationLevel3);
        _vertexShader = new VertexShader(_device, vertexCode);
        _pixelShader = new PixelShader(_device, pixelCode);
        using ShaderSignature signature = ShaderSignature.GetInputSignature(vertexCode);
        _inputLayout = new InputLayout(
            _device,
            signature,
            [
                new InputElement(
                    "POSITION",
                    0,
                    Format.R32G32_Float,
                    0,
                    0),
                new InputElement(
                    "COLOR",
                    0,
                    Format.R8G8B8A8_UNorm,
                    8,
                    0),
            ]);
        _rasterizer = new RasterizerState(
            _device,
            new RasterizerStateDescription
            {
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
                IsDepthClipEnabled = false,
            });
    }

    private void CreateRenderTarget(bool resizeBuffers)
    {
        if (_context is null || _swapChain is null || _device is null)
        {
            return;
        }
        _context.OutputMerger.SetRenderTargets((RenderTargetView?)null);
        _renderTarget?.Dispose();
        _renderTarget = null;
        if (resizeBuffers)
        {
            _swapChain.ResizeBuffers(
                2,
                Math.Max(1, ClientSize.Width),
                Math.Max(1, ClientSize.Height),
                Format.R8G8B8A8_UNorm,
                SwapChainFlags.None);
        }
        using Texture2D backBuffer = _swapChain.GetBackBuffer<Texture2D>(0);
        _renderTarget = new RenderTargetView(_device, backBuffer);
    }

    private void UploadCadBuffer()
    {
        _cadBuffer?.Dispose();
        _cadBuffer = null;
        _fillBuffer?.Dispose();
        _fillBuffer = null;
        if (_device is null || _drawing is null)
        {
            RequestFrame();
            return;
        }
        if (!_drawing.Vertices.IsEmpty)
        {
            if (!MemoryMarshal.TryGetArray(
                    _drawing.Vertices,
                    out ArraySegment<CadVertex> segment)
                || segment.Array is null
                || segment.Offset != 0
                || segment.Count != segment.Array.Length)
            {
                throw new InvalidOperationException("Packed CAD vertices must use one array.");
            }
            _cadBuffer = Buffer.Create(_device, BindFlags.VertexBuffer, segment.Array);
        }
        if (!_drawing.FillVertices.IsEmpty)
        {
            if (!MemoryMarshal.TryGetArray(
                    _drawing.FillVertices,
                    out ArraySegment<CadVertex> fills)
                || fills.Array is null
                || fills.Offset != 0
                || fills.Count != fills.Array.Length)
            {
                throw new InvalidOperationException("Packed CAD fill vertices must use one array.");
            }
            _fillBuffer = Buffer.Create(_device, BindFlags.VertexBuffer, fills.Array);
        }
        RequestFrame();
    }

    private void RenderFrame()
    {
        if (_context is null
            || _swapChain is null
            || _renderTarget is null
            || _inputLayout is null
            || _vertexShader is null
            || _pixelShader is null
            || _cameraBuffer is null
            || ClientSize.Width <= 0
            || ClientSize.Height <= 0)
        {
            return;
        }
        try
        {
            _dirty = false;
            _context.OutputMerger.SetRenderTargets(_renderTarget);
            _context.Rasterizer.SetViewport(
                0,
                0,
                ClientSize.Width,
                ClientSize.Height,
                0,
                1);
            _context.ClearRenderTargetView(
                _renderTarget,
                new RawColor4(32 / 255f, 32 / 255f, 32 / 255f, 1));
            _context.InputAssembler.InputLayout = _inputLayout;
            _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            _context.VertexShader.Set(_vertexShader);
            _context.VertexShader.SetConstantBuffer(0, _cameraBuffer);
            _context.PixelShader.Set(_pixelShader);
            if (_rasterizer is not null)
            {
                _context.Rasterizer.State = _rasterizer;
            }

            float halfWidth = Math.Max(
                Camera.UnitsPerPixel * ClientSize.Width * 0.5f,
                float.Epsilon);
            float halfHeight = Math.Max(
                Camera.UnitsPerPixel * ClientSize.Height * 0.5f,
                float.Epsilon);
            var camera = new Vector4(
                Camera.Center.X,
                Camera.Center.Y,
                halfWidth,
                halfHeight);
            _context.UpdateSubresource(ref camera, _cameraBuffer);

            if (_gridBuffer is not null)
            {
                if (_gridDirty)
                {
                    UpdateGridBuffer();
                }
                if (GridVisible)
                {
                    DrawOverlayRange(0, _gridVertexCount);
                }
            }

            if (_fillBuffer is not null && _drawing is not null)
            {
                _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                _context.InputAssembler.SetVertexBuffers(
                    0,
                    new VertexBufferBinding(
                        _fillBuffer,
                        CadVertex.SizeInBytes,
                        0));
                ReadOnlySpan<CadDrawRange> fillRanges = _drawing.FillDrawRanges.Span;
                for (int index = 0; index < fillRanges.Length; index++)
                {
                    CadDrawRange range = fillRanges[index];
                    if (range.VertexCount > 0
                        && IsLayerVisible(range.LayerId)
                        && IsLayerVisible(range.GateLayerId))
                    {
                        _context.Draw(range.VertexCount, range.StartVertex);
                    }
                }
            }

            if (_cadBuffer is not null && _drawing is not null)
            {
                _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
                _context.InputAssembler.SetVertexBuffers(
                    0,
                    new VertexBufferBinding(
                        _cadBuffer,
                        CadVertex.SizeInBytes,
                        0));
                ReadOnlySpan<CadDrawRange> ranges = _drawing.DrawRanges.Span;
                for (int index = 0; index < ranges.Length; index++)
                {
                    CadDrawRange range = ranges[index];
                    if (range.VertexCount > 0
                        && IsLayerVisible(range.LayerId)
                        && IsLayerVisible(range.GateLayerId))
                    {
                        _context.Draw(range.VertexCount, range.StartVertex);
                    }
                }
            }

            if (_gridBuffer is not null)
            {
                DrawOverlayRange(_accentVertexStart, _accentVertexCount);
            }
            if (_zoomDragging)
            {
                DrawZoomWindow();
            }
            _swapChain.Present(1, PresentFlags.None);
        }
        catch (SharpDXException exception)
        {
            RenderFailed?.Invoke(exception);
            TryRecoverDevice();
        }
    }

    private void DrawOverlayRange(int startVertex, int vertexCount)
    {
        if (_context is null || _gridBuffer is null || vertexCount <= 0)
        {
            return;
        }
        _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
        _context.InputAssembler.SetVertexBuffers(
            0,
            new VertexBufferBinding(
                _gridBuffer,
                CadVertex.SizeInBytes,
                0));
        _context.Draw(vertexCount, startVertex);
    }

    private void DrawZoomWindow()
    {
        if (_context is null || _zoomBuffer is null)
        {
            return;
        }
        Vector2 viewport = new(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        Vector2 a = Camera.ScreenToWorld(new Vector2(_zoomStart.X, _zoomStart.Y), viewport);
        Vector2 b = Camera.ScreenToWorld(new Vector2(_zoomCurrent.X, _zoomCurrent.Y), viewport);
        float left = MathF.Min(a.X, b.X);
        float right = MathF.Max(a.X, b.X);
        float bottom = MathF.Min(a.Y, b.Y);
        float top = MathF.Max(a.Y, b.Y);
        _zoomVertices[0] = new CadVertex(left, bottom, ZoomWindowColor);
        _zoomVertices[1] = new CadVertex(right, bottom, ZoomWindowColor);
        _zoomVertices[2] = new CadVertex(right, bottom, ZoomWindowColor);
        _zoomVertices[3] = new CadVertex(right, top, ZoomWindowColor);
        _zoomVertices[4] = new CadVertex(right, top, ZoomWindowColor);
        _zoomVertices[5] = new CadVertex(left, top, ZoomWindowColor);
        _zoomVertices[6] = new CadVertex(left, top, ZoomWindowColor);
        _zoomVertices[7] = new CadVertex(left, bottom, ZoomWindowColor);
        DataBox box = _context.MapSubresource(
            _zoomBuffer,
            0,
            MapMode.WriteDiscard,
            SharpDX.Direct3D11.MapFlags.None);
        Utilities.Write(box.DataPointer, _zoomVertices, 0, _zoomVertices.Length);
        _context.UnmapSubresource(_zoomBuffer, 0);
        _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
        _context.InputAssembler.SetVertexBuffers(
            0,
            new VertexBufferBinding(_zoomBuffer, CadVertex.SizeInBytes, 0));
        _context.Draw(_zoomVertices.Length, 0);
    }

    private void ApplyZoomWindow(Point start, Point end)
    {
        if (Math.Abs(end.X - start.X) < 4 || Math.Abs(end.Y - start.Y) < 4)
        {
            return;
        }
        Vector2 center = Camera.Center;
        float unitsPerPixel = Camera.UnitsPerPixel;
        Camera.FitScreenRect(
            new Vector2(start.X, start.Y),
            new Vector2(end.X, end.Y),
            new Vector2(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)),
            margin: 0f);
        _gridDirty = true;
        RequestFrame();
        CommitCameraUndo(center, unitsPerPixel);
    }

    private void UpdateGridBuffer()
    {
        _gridDirty = false;
        _gridVertexCount = 0;
        _accentVertexStart = 0;
        _accentVertexCount = 0;
        if (_context is null || _gridBuffer is null)
        {
            return;
        }
        ViewportOverlay.Counts counts = ViewportOverlay.Write(
            _gridVertices,
            Camera.Center,
            Camera.UnitsPerPixel,
            ClientSize.Width,
            ClientSize.Height);
        _gridVertexCount = counts.GridVertices;
        _accentVertexStart = counts.GridVertices;
        _accentVertexCount = counts.AccentVertices;
        int total = counts.Total;
        if (total <= 0)
        {
            return;
        }

        DataBox box = _context.MapSubresource(
            _gridBuffer,
            0,
            MapMode.WriteDiscard,
            SharpDX.Direct3D11.MapFlags.None);
        Utilities.Write(box.DataPointer, _gridVertices, 0, total);
        _context.UnmapSubresource(_gridBuffer, 0);
    }

    private void TryRecoverDevice()
    {
        try
        {
            CreateDeviceResources();
            UploadCadBuffer();
            RequestFrame();
        }
        catch (Exception exception)
        {
            RenderFailed?.Invoke(exception);
        }
    }

    private void RequestFrame() => _dirty = true;

    private void FlushWheelUndo()
    {
        _wheelUndoTimer.Stop();
        if (!_wheelUndoArmed)
        {
            return;
        }
        _wheelUndoArmed = false;
        CommitCameraUndo(_wheelCenter, _wheelUnitsPerPixel);
    }

    private void CommitCameraUndo(Vector2 previousCenter, float previousUnitsPerPixel)
    {
        if (Camera.Matches(previousCenter, previousUnitsPerPixel))
        {
            return;
        }
        CameraUndoCommitted?.Invoke(
            this,
            new CameraUndoEventArgs(Camera, previousCenter, previousUnitsPerPixel));
    }

    private void DisposeDeviceResources()
    {
        _context?.ClearState();
        _context?.Flush();
        _cadBuffer?.Dispose();
        _fillBuffer?.Dispose();
        _gridBuffer?.Dispose();
        _zoomBuffer?.Dispose();
        _cameraBuffer?.Dispose();
        _inputLayout?.Dispose();
        _rasterizer?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _renderTarget?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _cadBuffer = null;
        _fillBuffer = null;
        _gridBuffer = null;
        _zoomBuffer = null;
        _cameraBuffer = null;
        _inputLayout = null;
        _rasterizer = null;
        _vertexShader = null;
        _pixelShader = null;
        _renderTarget = null;
        _swapChain = null;
        _context = null;
        _device = null;
    }
}
