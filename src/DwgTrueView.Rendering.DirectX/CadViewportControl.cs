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

/// <summary>
/// A display-only Direct3D 11 CAD viewport. CAD entities occupy one immutable
/// GPU line-list vertex buffer; layer toggles select draw ranges without
/// rebuilding or copying geometry.
/// </summary>
public sealed class CadViewportControl : Control
{
    private const int MaximumGridVertices = 4096;
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
    private Buffer? _cameraBuffer;
    private Buffer? _cadBuffer;
    private Buffer? _gridBuffer;
    private PackedCadDrawing? _drawing;
    private bool[] _layerVisibility = [];
    private bool _dirty = true;
    private bool _gridDirty = true;
    private bool _panning;
    private Point _lastMouse;
    private int _gridVertexCount;
    private bool _disposed;
    private bool _gridVisible = true;

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
    }

    public ViewCamera2D Camera { get; } = new();
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

    public void LoadDrawing(PackedCadDrawing? drawing)
    {
        _drawing = drawing;
        _layerVisibility = drawing?.Layers.Span
            .ToArray()
            .Select(static layer => layer.IsInitiallyVisible)
            .ToArray()
            ?? [];
        UploadCadBuffer();
        ZoomExtents();
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

    public void ZoomExtents()
    {
        if (_drawing is not null)
        {
            Camera.Fit(
                _drawing.Bounds,
                new Vector2(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)));
        }
        _gridDirty = true;
        RequestFrame();
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
        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _lastMouse = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Capture = false;
            Cursor = Cursors.Cross;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            Point delta = new(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
            _lastMouse = e.Location;
            Camera.PanPixels(new Vector2(delta.X, delta.Y));
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
        float factor = MathF.Pow(0.85f, e.Delta / 120f);
        Camera.ZoomAt(
            new Vector2(e.X, e.Y),
            new Vector2(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)),
            factor);
        _gridDirty = true;
        RequestFrame();
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
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
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
        if (_device is null || _drawing is null || _drawing.Vertices.IsEmpty)
        {
            RequestFrame();
            return;
        }
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
                new RawColor4(30 / 255f, 30 / 255f, 30 / 255f, 1));
            _context.InputAssembler.InputLayout = _inputLayout;
            _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            _context.VertexShader.Set(_vertexShader);
            _context.VertexShader.SetConstantBuffer(0, _cameraBuffer);
            _context.PixelShader.Set(_pixelShader);

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

            if (GridVisible && _gridBuffer is not null)
            {
                if (_gridDirty)
                {
                    UpdateGridBuffer();
                }
                if (_gridVertexCount > 0)
                {
                    _context.InputAssembler.SetVertexBuffers(
                        0,
                        new VertexBufferBinding(
                            _gridBuffer,
                            CadVertex.SizeInBytes,
                            0));
                    _context.Draw(_gridVertexCount, 0);
                }
            }

            if (_cadBuffer is not null && _drawing is not null)
            {
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
            _swapChain.Present(1, PresentFlags.None);
        }
        catch (SharpDXException exception)
        {
            RenderFailed?.Invoke(exception);
            TryRecoverDevice();
        }
    }

    private void UpdateGridBuffer()
    {
        _gridDirty = false;
        _gridVertexCount = 0;
        if (_context is null || _gridBuffer is null)
        {
            return;
        }
        float halfWidth = Camera.UnitsPerPixel * ClientSize.Width * 0.5f;
        float halfHeight = Camera.UnitsPerPixel * ClientSize.Height * 0.5f;
        float left = Camera.Center.X - halfWidth;
        float right = Camera.Center.X + halfWidth;
        float bottom = Camera.Center.Y - halfHeight;
        float top = Camera.Center.Y + halfHeight;
        float step = NiceGridStep(Camera.UnitsPerPixel * 80);
        uint color = CadVertex.FromArgb(unchecked((int)0xFF333941));
        float firstX = MathF.Floor(left / step) * step;
        for (float x = firstX;
             x <= right && _gridVertexCount + 2 <= MaximumGridVertices;
             x += step)
        {
            _gridVertices[_gridVertexCount++] = new CadVertex(x, bottom, color);
            _gridVertices[_gridVertexCount++] = new CadVertex(x, top, color);
        }
        float firstY = MathF.Floor(bottom / step) * step;
        for (float y = firstY;
             y <= top && _gridVertexCount + 2 <= MaximumGridVertices;
             y += step)
        {
            _gridVertices[_gridVertexCount++] = new CadVertex(left, y, color);
            _gridVertices[_gridVertexCount++] = new CadVertex(right, y, color);
        }

        DataBox box = _context.MapSubresource(
            _gridBuffer,
            0,
            MapMode.WriteDiscard,
            SharpDX.Direct3D11.MapFlags.None);
        Utilities.Write(box.DataPointer, _gridVertices, 0, _gridVertexCount);
        _context.UnmapSubresource(_gridBuffer, 0);
    }

    private static float NiceGridStep(float target)
    {
        target = Math.Max(target, 1e-9f);
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(target)));
        float normalized = target / magnitude;
        float multiplier = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 5
                    ? 5
                    : 10;
        return magnitude * multiplier;
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

    private void DisposeDeviceResources()
    {
        _context?.ClearState();
        _context?.Flush();
        _cadBuffer?.Dispose();
        _gridBuffer?.Dispose();
        _cameraBuffer?.Dispose();
        _inputLayout?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _renderTarget?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _cadBuffer = null;
        _gridBuffer = null;
        _cameraBuffer = null;
        _inputLayout = null;
        _vertexShader = null;
        _pixelShader = null;
        _renderTarget = null;
        _swapChain = null;
        _context = null;
        _device = null;
    }
}
