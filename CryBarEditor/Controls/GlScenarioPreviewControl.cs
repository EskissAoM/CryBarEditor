using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using CryBarEditor.Classes;
using static Avalonia.OpenGL.GlConsts;

namespace CryBarEditor.Controls;

public class GlScenarioPreviewControl : OpenGlControlBase, ICustomHitTest
{
    // Constants Avalonia's GlConsts doesn't surface
    const int GL_LINES                = 0x0001;
    const int GL_TRIANGLES            = 0x0004;
    const int GL_DYNAMIC_DRAW         = 0x88E8;
    const int GL_TEXTURE0             = 0x84C0;
    const int GL_TEXTURE_2D_ARRAY     = 0x8C1A;
    const int GL_LINEAR               = 0x2601;
    const int GL_LINEAR_MIPMAP_LINEAR = 0x2703;
    const int GL_REPEAT               = 0x2901;
    const int GL_TEXTURE_WRAP_S       = 0x2802;
    const int GL_TEXTURE_WRAP_T       = 0x2803;
    const int GL_TEXTURE_MIN_FILTER   = 0x2801;
    const int GL_TEXTURE_MAG_FILTER   = 0x2800;
    const int GL_RGBA                 = 0x1908;
    const int GL_RGBA8                = 0x8058;
    const int GL_UNSIGNED_BYTE        = 0x1401;
    const int GL_FLOAT_TYPE           = 0x1406;
    const int GL_UNSIGNED_INT_TYPE    = 0x1405;
    const int GL_BLEND                = 0x0BE2;
    const int GL_SRC_ALPHA            = 0x0302;
    const int GL_ONE_MINUS_SRC_ALPHA  = 0x0303;

    bool ICustomHitTest.HitTest(Point point) => Bounds.Contains(point);

    // Grass-green; visible until ScenarioTextureLoader replaces a slice.
    const byte PlaceholderR = 0x4E, PlaceholderG = 0x6B, PlaceholderB = 0x33, PlaceholderA = 0xFF;

    const int SliceSize = 256;
    const int SliceBytes = SliceSize * SliceSize * 4;

    readonly OrbitCamera _camera = new();
    ScenarioPreviewData? _data;
    bool _meshUploaded;
    bool _showEntities = true;
    bool _showWater = true;

    public bool ShowEntities
    {
        get => _showEntities;
        set
        {
            if (_showEntities == value) return;
            _showEntities = value;
            RequestNextFrameRendering();
        }
    }

    public bool ShowWater
    {
        get => _showWater;
        set
        {
            if (_showWater == value) return;
            _showWater = value;
            RequestNextFrameRendering();
        }
    }

    int _heightProgram;
    int _heightVao, _heightVbo, _heightEbo;
    int _uMvp, _uTexArray, _uYScale;

    int _waterProgram;
    int _waterVao, _waterVbo, _waterEbo;
    int _uWaterMvp, _uWaterYScale;
    int _waterIndexCount;
    bool _waterUploaded;

    int _tileSelectProgram;
    int _tileSelectVao, _tileSelectVbo;
    int _uTileSelectMvp, _uTileSelectYScale, _uTileSelectColor;
    int _tileSelectVertexCount;
    bool _tileSelectDirty;

    int _entitySelectProgram;
    int _entitySelectVao, _entitySelectQuadVbo, _entitySelectInstanceVbo;
    int _uEntitySelectView, _uEntitySelectProj, _uEntitySelectSize, _uEntitySelectYScale;
    int _entitySelectInstanceCount;
    bool _entitySelectDirty;

    // Yaw arrow: 2-vertex line per entity along forward; reuses _entitySelectDirty.
    int _yawArrowProgram;
    int _yawArrowVao, _yawArrowVbo;
    int _uYawArrowMvp, _uYawArrowYScale;
    int _yawArrowVertexCount;

    int _billboardProgram;
    int _billboardVao, _billboardQuadVbo, _billboardInstanceVbo;
    int _uBillboardView, _uBillboardProj, _uBillboardSize, _uBillboardCamPos, _uBillboardFadeNear, _uBillboardFadeFar, _uBillboardYScale;
    bool _entitiesUploaded;
    int _entityCount;
    float _avgHeight;

    int _texArray;
    int _allocatedSlices;

    // Restored on realloc so a new-slot grow doesn't drop existing slices to placeholder
    // until the host's full reload finishes re-decoding every DDT.
    byte[]?[] _cachedSlices = [];

    bool _glInitialized;

    bool _leftDragging, _rightDragging;
    bool _leftDragMoved, _rightDragMoved;
    Avalonia.Point _lastPointerPos;
    Avalonia.Point _leftPressPos;
    Avalonia.Point _rightPressPos;

    // Drag-to-move: HoldMs hold on a selected entity arms drag mode.
    DispatcherTimer? _holdTimer;
    bool _holdArmed;
    bool _moveMode;
    Avalonia.Point _pressScreenPos;
    Vector3 _moveAnchorWorld;
    readonly Dictionary<uint, Vector3> _moveOldPositions = new();
    readonly Dictionary<uint, Vector3> _previewOffset = new();
    bool _windowDeactivateHooked;

    // Drag-to-rotate: HoldMs RIGHT hold on a selected entity arms rotate mode.
    DispatcherTimer? _rotateHoldTimer;
    bool _rotateHoldArmed;
    bool _rotateMode;
    Avalonia.Point _rotatePressScreenPos;
    readonly Dictionary<uint, float> _rotateOldYaws = new();
    float _rotatePreviewDelta; // degrees, applied to all rotating entities

    const int HoldMs = 400;
    const float RotateDegPerPixel = 0.5f;
    static readonly (float R, float G, float B, float A) RingColorMove = (0.20f, 0.80f, 1.00f, 1.0f);
    static readonly (float R, float G, float B, float A) RingColorRotate = (0.40f, 1.00f, 0.40f, 1.0f);
    static readonly (float R, float G, float B, float A) RingColorDefault = (1.00f, 0.82f, 0.30f, 1.0f);
    (float R, float G, float B, float A) _ringColor = RingColorDefault;

    public event Action<IScenarioCommand?>? GestureCommitted;

    // Fires on runtime reallocation only (not initial alloc). The realloc wipes
    // slices to placeholder, so the host must re-run texture loading.
    public event Action? TextureArrayResized;

    readonly ConcurrentQueue<Action<GlInterface>> _glActionQueue = new();

    // Function pointers for GL calls Avalonia's GlInterface doesn't expose.
    unsafe delegate* unmanaged<int, int, int, int, int, int, int, int, int, void*, void> _glTexImage3D;
    unsafe delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void*, void> _glTexSubImage3D;
    unsafe delegate* unmanaged<uint, uint, void> _glBlendFunc;
    unsafe delegate* unmanaged<uint, uint, void> _glVertexAttribDivisor;
    unsafe delegate* unmanaged<uint, int, int, int, void> _glDrawArraysInstanced;
    unsafe delegate* unmanaged<int, float, float, float, void> _glUniform3f;
    unsafe delegate* unmanaged<float, void> _glLineWidth;
    unsafe delegate* unmanaged<int, float, float, float, float, void> _glUniform4f;

    Window? _hookedWindow;

    public GlScenarioPreviewControl()
    {
        AttachedToVisualTree += OnAttachedToVisualTree_DragMove;
        DetachedFromVisualTree += OnDetachedFromVisualTree_DragMove;
    }

    void OnAttachedToVisualTree_DragMove(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_windowDeactivateHooked) return;
        if (TopLevel.GetTopLevel(this) is Window w)
        {
            w.Deactivated += OnTopLevelDeactivated;
            _hookedWindow = w;
            _windowDeactivateHooked = true;
        }
    }

    void OnDetachedFromVisualTree_DragMove(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_windowDeactivateHooked) return;
        if (_hookedWindow is not null)
            _hookedWindow.Deactivated -= OnTopLevelDeactivated;
        _hookedWindow = null;
        _windowDeactivateHooked = false;
    }

    void OnTopLevelDeactivated(object? sender, EventArgs e)
    {
        if (_moveMode || _holdArmed) CancelMoveMode();
        if (_rotateMode || _rotateHoldArmed) CancelRotateMode();
    }

    public void QueueGlAction(Action<GlInterface> action)
    {
        _glActionQueue.Enqueue(action);
        RequestNextFrameRendering();
    }

    public void SetScenario(ScenarioPreviewData? data)
    {
        if (_data is not null)
            _data.Selection.Changed -= OnSelectionChanged;

        // Cancel any in-flight gesture so cursor / ring color / preview offsets
        // and timer state don't leak across scenarios.
        if (_moveMode || _holdArmed) CancelMoveMode();
        if (_rotateMode || _rotateHoldArmed) CancelRotateMode();
        _previewOffset.Clear();

        _data = data;
        _entityCount = 0;
        _meshUploaded = false;
        _waterUploaded = false;
        _entitiesUploaded = false;
        _tileSelectDirty = true;
        _entitySelectDirty = true;
        _hasEmittedCursorHit = false;
        _lastEmittedCursorHit = null;
        _cachedSlices = [];
        _allocatedSlices = 0;
        if (data is not null)
        {
            data.Selection.Changed += OnSelectionChanged;

            float cx = data.Terrain.MapSizeX * 0.5f;
            float cz = data.Terrain.MapSizeZ * 0.5f;
            float radius = MathF.Max(data.Terrain.MapSizeX, data.Terrain.MapSizeZ) * 0.55f;
            _camera.FitToSphere(cx, 0f, cz, radius);
            _camera.Azimuth = 232f;

            double sum = 0;
            for (int i = 0; i < data.Terrain.Heights.Length; i++) sum += data.Terrain.Heights[i];
            _avgHeight = data.Terrain.Heights.Length > 0 ? (float)(sum / data.Terrain.Heights.Length) : 0f;
        }
        else
        {
            _avgHeight = 0f;
        }
        RequestNextFrameRendering();
    }

    void OnSelectionChanged()
    {
        _tileSelectDirty = true;
        _entitySelectDirty = true;
        RequestNextFrameRendering();
    }

    // Called by the editor host after a command Apply/Undo/Redo. Reuses the
    // existing TextureSet so slice indices stay aligned with already-uploaded slices.
    public void OnDataMutated(RenderHint hint)
    {
        if (hint == RenderHint.None) return;

        if (_data is not null && (hint & (RenderHint.TerrainTexture | RenderHint.TerrainGeometry)) != 0)
        {
            _data.TerrainMesh = TerrainMeshBuilder.Build(_data.Terrain, _data.TextureSet);
            _meshUploaded = false;
        }

        // Water mesh tracks heights AND per-tile water type.
        if (_data is not null && (hint & (RenderHint.TerrainWater | RenderHint.TerrainGeometry)) != 0)
        {
            _data.WaterMesh = WaterMeshBuilder.Build(_data.Terrain);
            _waterUploaded = false;
        }

        if ((hint & (RenderHint.EntityList | RenderHint.EntityField)) != 0)
        {
            _entitiesUploaded = false;
            _entitySelectDirty = true;
        }

        // Tile selection ring reads vertex heights; geometry edits invalidate it.
        if ((hint & RenderHint.TerrainGeometry) != 0)
            _tileSelectDirty = true;

        RequestNextFrameRendering();
    }

    const string VertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        // Per-vertex height-field slope (dh/dx, dh/dz). Smoothly interpolated so the
        // fragment shader can derive a continuous normal across triangle boundaries.
        layout(location = 1) in vec2 aSlope;
        layout(location = 2) in vec4 aSlices;
        layout(location = 3) in vec3 aWeights;

        uniform mat4 uMVP;
        uniform float uYScale;

        out vec3 vWorld;
        out vec2 vSlope;
        // Slice indices use flat interpolation -- linear interpolation produces
        // fractional values that round to the wrong slice between vertices, sampling
        // neighbor tiles' textures and producing the colored bleed-through.
        flat out vec4 vSlices;
        out vec4 vWeights;

        void main() {
            vec3 p = vec3(aPos.x, aPos.y * uYScale, aPos.z);
            gl_Position = uMVP * vec4(p, 1.0);
            vWorld = p;
            // Bake the Y-scale into the slope so the fragment shader only sees one
            // already-correct value -- avoids declaring uYScale in both stages.
            vSlope = aSlope * uYScale;
            vSlices = aSlices;
            // 4th weight is implicit so the data fits in 12 floats per vertex
            float w4 = clamp(1.0 - aWeights.x - aWeights.y - aWeights.z, 0.0, 1.0);
            vWeights = vec4(aWeights, w4);
        }
        """;

    const string BillboardVertexShaderBody = """
        layout(location = 0) in vec2 aQuad;
        layout(location = 1) in vec3 aWorldPos;
        layout(location = 2) in vec4 aColor;
        layout(location = 3) in float aUnderwater;
        uniform mat4 uView;
        uniform mat4 uProj;
        uniform float uSize;
        uniform vec3 uCamPos;
        uniform float uFadeNear;
        uniform float uFadeFar;
        uniform float uYScale;
        out vec2 vUv;
        out vec4 vColor;
        out float vFade;
        out float vUnderwater;
        void main() {
            // Apply the same Y scale as terrain, then lift above the (already scaled) ground.
            vec3 wp = vec3(aWorldPos.x, aWorldPos.y * uYScale + 0.4, aWorldPos.z);
            // World-anchored billboard: offset in view-space (X right, Y up relative
            // to camera) so the disc has a constant world radius and zooms naturally
            // with the terrain instead of staying fixed-pixel.
            vec4 viewPos = uView * vec4(wp, 1.0);
            viewPos.xy += aQuad * uSize;
            gl_Position = uProj * viewPos;
            vUv = aQuad;
            vColor = aColor;
            vUnderwater = aUnderwater;
            float d = distance(uCamPos, wp);
            vFade = 1.0 - smoothstep(uFadeNear, uFadeFar, d);
        }
        """;

    const string BillboardFragmentShaderBody = """
        in vec2 vUv;
        in vec4 vColor;
        in float vFade;
        in float vUnderwater;
        out vec4 fragColor;
        void main() {
            float r = length(vUv);
            if (r > 1.0) discard;
            float edge = smoothstep(0.92, 1.0, r);
            vec4 col = mix(vColor, vec4(0.0, 0.0, 0.0, 1.0), edge);
            vec3 waterTint = vec3(0.20, 0.40, 0.55);
            col.rgb = mix(col.rgb, mix(col.rgb, waterTint, 0.55), vUnderwater);
            col.a *= mix(0.35, 1.0, clamp(vFade, 0.0, 1.0));
            fragColor = col;
        }
        """;

    const string WaterVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMVP;
        uniform float uYScale;
        void main() { gl_Position = uMVP * vec4(aPos.x, aPos.y * uYScale, aPos.z, 1.0); }
        """;

    const string WaterFragmentShaderBody = """
        out vec4 fragColor;
        void main() { fragColor = vec4(0.20, 0.40, 0.55, 0.55); }
        """;

    const string FragmentShaderBody = """
        in vec3 vWorld;
        in vec2 vSlope;
        flat in vec4 vSlices;
        in vec4 vWeights;

        uniform sampler2DArray uTexArray;

        out vec4 fragColor;

        void main() {
            // Texture repeats every TileScale tiles. AoMR scales textures to span
            // multiple tiles so the same data isn't visibly tiled per cell; 0.25
            // (=> 4 tiles per repeat) reads close to the in-game grain.
            const float TileScale = 0.25;
            vec2 uv = fract(vWorld.xz * TileScale);

            // Round interpolated slice index so each fragment samples whole slices;
            // smooth cross-tile blending would need flat-shaded slices + unshared verts.
            float sA = max(floor(vSlices.x + 0.5), 0.0);
            float sB = max(floor(vSlices.y + 0.5), 0.0);
            float sC = max(floor(vSlices.z + 0.5), 0.0);
            float sD = max(floor(vSlices.w + 0.5), 0.0);

            vec4 a = texture(uTexArray, vec3(uv, sA));
            vec4 b = texture(uTexArray, vec3(uv, sB));
            vec4 c = texture(uTexArray, vec3(uv, sC));
            vec4 d = texture(uTexArray, vec3(uv, sD));

            float wSum = vWeights.x + vWeights.y + vWeights.z + vWeights.w;
            wSum = max(wSum, 1e-4);
            vec4 col = (a * vWeights.x + b * vWeights.y + c * vWeights.z + d * vWeights.w) / wSum;

            // Smooth-shaded normal from the interpolated per-vertex slope.
            // vSlope already carries the Y-scale baked in by the vertex shader.
            vec3 N = normalize(vec3(-vSlope.x, 1.0, -vSlope.y));

            // Sun comes from above the (0,0) corner -- the camera-side corner
            // matching the in-game default view -- so shadows fall on the
            // far side of peaks instead of wrapping all around.
            vec3 L = normalize(vec3(-0.55, 0.65, -0.55));
            float NdotL = max(dot(N, L), 0.0);
            float lighting = mix(0.4, 1.05, NdotL);
            fragColor = vec4(min(col.rgb * lighting, vec3(1.0)), 1.0);
        }
        """;

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        bool isGles = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
        string vsPreamble = isGles ? "#version 300 es\n" : "#version 330 core\n";
        // sampler2DArray is opaque, so GLSL ES 3.00 requires an explicit precision
        // qualifier; without it the heightmap program silently fails to link and
        // the terrain renders nothing (canvas stays at clear color).
        string fsPreamble = isGles
            ? "#version 300 es\nprecision mediump float;\nprecision mediump sampler2DArray;\n"
            : "#version 330 core\n";

        _glTexImage3D    = (delegate* unmanaged<int, int, int, int, int, int, int, int, int, void*, void>)gl.GetProcAddress("glTexImage3D");
        _glTexSubImage3D = (delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void*, void>)gl.GetProcAddress("glTexSubImage3D");
        _glBlendFunc = (delegate* unmanaged<uint, uint, void>)gl.GetProcAddress("glBlendFunc");
        _glVertexAttribDivisor = (delegate* unmanaged<uint, uint, void>)gl.GetProcAddress("glVertexAttribDivisor");
        _glDrawArraysInstanced = (delegate* unmanaged<uint, int, int, int, void>)gl.GetProcAddress("glDrawArraysInstanced");
        _glUniform3f = (delegate* unmanaged<int, float, float, float, void>)gl.GetProcAddress("glUniform3f");
        _glUniform4f = (delegate* unmanaged<int, float, float, float, float, void>)gl.GetProcAddress("glUniform4f");
        _glLineWidth = (delegate* unmanaged<float, void>)gl.GetProcAddress("glLineWidth");

        string?[] missing =
        [
            _glTexImage3D == null ? "glTexImage3D" : null,
            _glTexSubImage3D == null ? "glTexSubImage3D" : null,
            _glDrawArraysInstanced == null ? "glDrawArraysInstanced" : null,
            _glVertexAttribDivisor == null ? "glVertexAttribDivisor" : null,
        ];
        var missingNames = string.Join(", ", missing.Where(s => s != null));
        if (missingNames.Length > 0)
        {
            var ctxInfo = $"GL {gl.ContextInfo.Version.Type} {gl.ContextInfo.Version.Major}.{gl.ContextInfo.Version.Minor}";
            RaiseError($"GL procs missing on this context ({ctxInfo}): {missingNames}");
        }

        _heightProgram = CreateProgram(gl, vsPreamble + VertexShaderBody, fsPreamble + FragmentShaderBody);
        _uMvp      = gl.GetUniformLocationString(_heightProgram, "uMVP");
        _uTexArray = gl.GetUniformLocationString(_heightProgram, "uTexArray");
        _uYScale   = gl.GetUniformLocationString(_heightProgram, "uYScale");

        _heightVao = gl.GenVertexArray();
        _heightVbo = gl.GenBuffer();
        _heightEbo = gl.GenBuffer();

        _texArray = gl.GenTexture();

        _waterProgram = CreateProgram(gl, vsPreamble + WaterVertexShaderBody, fsPreamble + WaterFragmentShaderBody);
        _uWaterMvp    = gl.GetUniformLocationString(_waterProgram, "uMVP");
        _uWaterYScale = gl.GetUniformLocationString(_waterProgram, "uYScale");
        _waterVao = gl.GenVertexArray();
        _waterVbo = gl.GenBuffer();
        _waterEbo = gl.GenBuffer();

        const string TileSelectVsBody = """
            layout(location = 0) in vec3 aPos;
            uniform mat4 uMVP;
            uniform float uYScale;
            void main()
            {
                vec3 p = aPos; p.y *= uYScale;
                // Slight upward bias to avoid z-fighting against the textured ground.
                p.y += 0.02;
                gl_Position = uMVP * vec4(p, 1.0);
            }
            """;
        const string TileSelectFsBody = """
            out vec4 fragColor;
            uniform vec4 uColor;
            void main() { fragColor = uColor; }
            """;
        _tileSelectProgram = CreateProgram(gl, vsPreamble + TileSelectVsBody, fsPreamble + TileSelectFsBody);
        _uTileSelectMvp    = gl.GetUniformLocationString(_tileSelectProgram, "uMVP");
        _uTileSelectYScale = gl.GetUniformLocationString(_tileSelectProgram, "uYScale");
        _uTileSelectColor  = gl.GetUniformLocationString(_tileSelectProgram, "uColor");

        _tileSelectVao = gl.GenVertexArray();
        _tileSelectVbo = gl.GenBuffer();

        const string EntitySelectVsBody = """
            layout(location = 0) in vec2 aQuad;
            layout(location = 1) in vec3 aInstancePos;
            layout(location = 2) in vec4 aInstanceColor;
            uniform mat4 uView;
            uniform mat4 uProj;
            uniform float uSize;
            uniform float uYScale;
            out vec2 vQuad;
            out vec4 vColor;
            void main()
            {
                vec3 wp = vec3(aInstancePos.x, aInstancePos.y * uYScale + 0.4, aInstancePos.z);
                vec4 vp = uView * vec4(wp, 1.0);
                // Slightly larger than the billboard half-radius for a visible ring.
                vp.xy += aQuad * uSize * 1.35;
                gl_Position = uProj * vp;
                vQuad = aQuad;
                vColor = aInstanceColor;
            }
            """;
        const string EntitySelectFsBody = """
            in vec2 vQuad;
            in vec4 vColor;
            out vec4 fragColor;
            void main()
            {
                float r = length(vQuad);
                // Ring band: outer 1.00, inner 0.82, with smooth AA edges.
                float outer = 1.00, inner = 0.82;
                if (r > outer || r < inner) discard;
                float aa = smoothstep(outer, outer - 0.04, r) * smoothstep(inner, inner + 0.04, r);
                fragColor = vec4(vColor.rgb, vColor.a * aa);
            }
            """;
        _entitySelectProgram = CreateProgram(gl, vsPreamble + EntitySelectVsBody, fsPreamble + EntitySelectFsBody);
        _uEntitySelectView   = gl.GetUniformLocationString(_entitySelectProgram, "uView");
        _uEntitySelectProj   = gl.GetUniformLocationString(_entitySelectProgram, "uProj");
        _uEntitySelectSize   = gl.GetUniformLocationString(_entitySelectProgram, "uSize");
        _uEntitySelectYScale = gl.GetUniformLocationString(_entitySelectProgram, "uYScale");

        _entitySelectVao = gl.GenVertexArray();
        _entitySelectQuadVbo = gl.GenBuffer();
        _entitySelectInstanceVbo = gl.GenBuffer();

        // Quad vertices: same -1..1 NDC quad (6 verts, two triangles) used for the billboard.
        unsafe
        {
            var quad = new float[] { -1, -1,  1, -1,  1, 1,  -1, -1,  1, 1,  -1, 1 };
            gl.BindVertexArray(_entitySelectVao);
            gl.BindBuffer(GL_ARRAY_BUFFER, _entitySelectQuadVbo);
            fixed (float* p = quad)
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(quad.Length * sizeof(float)), (IntPtr)p, GL_STATIC_DRAW);
            gl.BindVertexArray(0);
        }

        const string YawArrowVsBody = """
            layout(location = 0) in vec3 aPos;
            layout(location = 1) in vec4 aColor;
            uniform mat4 uMvp;
            uniform float uYScale;
            out vec4 vColor;
            void main()
            {
                // Discs sit at +0.4; the small extra lift keeps the arrow from
                // z-fighting its own disc in top-down views. Other discs in front still
                // occlude the arrow since they write depth at their (closer) center.
                gl_Position = uMvp * vec4(aPos.x, aPos.y * uYScale + 0.42, aPos.z, 1.0);
                vColor = aColor;
            }
            """;
        const string YawArrowFsBody = """
            in vec4 vColor;
            out vec4 fragColor;
            void main() { fragColor = vColor; }
            """;
        _yawArrowProgram = CreateProgram(gl, vsPreamble + YawArrowVsBody, fsPreamble + YawArrowFsBody);
        _uYawArrowMvp    = gl.GetUniformLocationString(_yawArrowProgram, "uMvp");
        _uYawArrowYScale = gl.GetUniformLocationString(_yawArrowProgram, "uYScale");
        _yawArrowVao = gl.GenVertexArray();
        _yawArrowVbo = gl.GenBuffer();

        _billboardProgram = CreateProgram(gl, vsPreamble + BillboardVertexShaderBody, fsPreamble + BillboardFragmentShaderBody);
        _uBillboardView     = gl.GetUniformLocationString(_billboardProgram, "uView");
        _uBillboardProj     = gl.GetUniformLocationString(_billboardProgram, "uProj");
        _uBillboardSize     = gl.GetUniformLocationString(_billboardProgram, "uSize");
        _uBillboardCamPos   = gl.GetUniformLocationString(_billboardProgram, "uCamPos");
        _uBillboardFadeNear = gl.GetUniformLocationString(_billboardProgram, "uFadeNear");
        _uBillboardFadeFar  = gl.GetUniformLocationString(_billboardProgram, "uFadeFar");
        _uBillboardYScale   = gl.GetUniformLocationString(_billboardProgram, "uYScale");
        _billboardVao = gl.GenVertexArray();
        _billboardQuadVbo = gl.GenBuffer();
        _billboardInstanceVbo = gl.GenBuffer();
        InitBillboardQuad(gl);

        _glInitialized = true;
    }

    unsafe void InitBillboardQuad(GlInterface gl)
    {
        var quad = new float[]
        {
            -1f, -1f,   1f, -1f,   -1f,  1f,
             1f, -1f,   1f,  1f,   -1f,  1f,
        };
        gl.BindVertexArray(_billboardVao);
        gl.BindBuffer(GL_ARRAY_BUFFER, _billboardQuadVbo);
        fixed (float* p = quad)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(quad.Length * sizeof(float)), (IntPtr)p, GL_STATIC_DRAW);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, GL_FLOAT_TYPE, 0, 2 * sizeof(float), IntPtr.Zero);
        gl.BindVertexArray(0);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // Notify hosts first so they drop their GL-bound caches before tear-down.
        GlContextLost?.Invoke();

        if (_heightProgram != 0) { gl.DeleteProgram(_heightProgram); _heightProgram = 0; }
        if (_heightVbo != 0)     { gl.DeleteBuffer(_heightVbo); _heightVbo = 0; }
        if (_heightEbo != 0)     { gl.DeleteBuffer(_heightEbo); _heightEbo = 0; }
        if (_heightVao != 0)     { gl.DeleteVertexArray(_heightVao); _heightVao = 0; }
        if (_texArray != 0)      { gl.DeleteTexture(_texArray); _texArray = 0; }
        if (_waterProgram != 0)  { gl.DeleteProgram(_waterProgram); _waterProgram = 0; }
        if (_waterVbo != 0)      { gl.DeleteBuffer(_waterVbo); _waterVbo = 0; }
        if (_waterEbo != 0)      { gl.DeleteBuffer(_waterEbo); _waterEbo = 0; }
        if (_waterVao != 0)      { gl.DeleteVertexArray(_waterVao); _waterVao = 0; }
        if (_tileSelectProgram != 0) { gl.DeleteProgram(_tileSelectProgram); _tileSelectProgram = 0; }
        if (_tileSelectVbo != 0)     { gl.DeleteBuffer(_tileSelectVbo); _tileSelectVbo = 0; }
        if (_tileSelectVao != 0)     { gl.DeleteVertexArray(_tileSelectVao); _tileSelectVao = 0; }
        _tileSelectVertexCount = 0;
        _tileSelectDirty = false;
        if (_entitySelectProgram != 0)      { gl.DeleteProgram(_entitySelectProgram); _entitySelectProgram = 0; }
        if (_entitySelectQuadVbo != 0)      { gl.DeleteBuffer(_entitySelectQuadVbo); _entitySelectQuadVbo = 0; }
        if (_entitySelectInstanceVbo != 0)  { gl.DeleteBuffer(_entitySelectInstanceVbo); _entitySelectInstanceVbo = 0; }
        if (_entitySelectVao != 0)          { gl.DeleteVertexArray(_entitySelectVao); _entitySelectVao = 0; }
        _entitySelectInstanceCount = 0;
        _entitySelectDirty = false;
        if (_yawArrowProgram != 0) { gl.DeleteProgram(_yawArrowProgram); _yawArrowProgram = 0; }
        if (_yawArrowVbo != 0)     { gl.DeleteBuffer(_yawArrowVbo); _yawArrowVbo = 0; }
        if (_yawArrowVao != 0)     { gl.DeleteVertexArray(_yawArrowVao); _yawArrowVao = 0; }
        _yawArrowVertexCount = 0;
        if (_billboardProgram != 0)     { gl.DeleteProgram(_billboardProgram); _billboardProgram = 0; }
        if (_billboardQuadVbo != 0)     { gl.DeleteBuffer(_billboardQuadVbo); _billboardQuadVbo = 0; }
        if (_billboardInstanceVbo != 0) { gl.DeleteBuffer(_billboardInstanceVbo); _billboardInstanceVbo = 0; }
        if (_billboardVao != 0)         { gl.DeleteVertexArray(_billboardVao); _billboardVao = 0; }

        _glInitialized = false;
        _meshUploaded = false;
        _waterUploaded = false;
        _entitiesUploaded = false;
        _entityCount = 0;
        _allocatedSlices = 0;
        _tileSelectVertexCount = 0;

        // Drain so any in-flight UploadSliceAsync TCS resolves instead of
        // hanging forever when no further render frames will run.
        while (_glActionQueue.TryDequeue(out var pending))
            pending(gl);
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        gl.BindFramebuffer(GL_FRAMEBUFFER, fb);

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = (int)(Bounds.Width * scaling);
        int h = (int)(Bounds.Height * scaling);
        if (w <= 0 || h <= 0) return;

        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0.05f, 0.06f, 0.08f, 1.0f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        if (_data is null || !_glInitialized)
        {
            // Invoke pending actions so their TaskCompletionSources resolve;
            // each action self-guards against missing GL state.
            while (_glActionQueue.TryDequeue(out var pending))
                pending(gl);
            return;
        }

        // Allocate texture array once per scenario (or whenever slice count grew).
        // _meshUploaded is flipped on edits to force mesh re-upload, but it must NOT
        // re-allocate the texture array -- that would wipe all loaded DDT slices back
        // to the olive placeholder. Track allocation separately. When an existing
        // array is grown (slice count increased after EnsureSlot append), all
        // previous slices ARE wiped to placeholder by the realloc -- in that case
        // we raise TextureArrayResized so the host kicks a full reload.
        int neededSlices = Math.Max(1, _data.TextureSet.Names.Count);
        if (_allocatedSlices < neededSlices)
        {
            bool wasGrow = _allocatedSlices > 0;
            EnsureTextureArrayAllocated(gl, _data);
            if (wasGrow)
            {
                var ev = TextureArrayResized;
                if (ev is not null)
                    Dispatcher.UIThread.Post(() => ev());
            }
        }
        if (!_meshUploaded)
        {
            UploadMesh(gl, _data);
            _meshUploaded = true;
        }

        while (_glActionQueue.TryDequeue(out var pending))
            pending(gl);

        gl.Enable(GL_DEPTH_TEST);

        float aspect = (float)w / h;
        var view = _camera.GetViewMatrix(out var eyePos);
        var proj = _camera.GetProjectionMatrix(aspect);
        var mvp = view * proj;
        var mvpCopy = mvp;

        gl.UseProgram(_heightProgram);
        gl.UniformMatrix4fv(_uMvp, 1, false, &mvpCopy.M11);
        gl.Uniform1f(_uYScale, HeightScale);

        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D_ARRAY, _texArray);
        gl.Uniform1i(_uTexArray, 0);

        gl.BindVertexArray(_heightVao);
        gl.DrawElements(GL_TRIANGLES, _data.TerrainMesh.Indices.Length, GL_UNSIGNED_INT_TYPE, IntPtr.Zero);

        if (_showWater && _data.WaterMesh is not null)
        {
            if (!_waterUploaded)
            {
                UploadWaterMesh(gl, _data);
                _waterUploaded = true;
            }
            DrawWater(gl, mvpCopy);
        }

        if (_tileSelectDirty)
        {
            UploadTileSelectionMesh(gl);
            _tileSelectDirty = false;
        }
        DrawTileSelection(gl, mvpCopy);

        if (_showEntities)
        {
            if (!_entitiesUploaded)
            {
                UploadEntities(gl, _data);
                // Yaw arrows track per-entity rotation and position; same upload
                // cadence as the billboard instance buffer.
                UploadYawArrows(gl);
                _entitiesUploaded = true;
            }
            if (_entityCount > 0)
                DrawEntities(gl, view, proj, eyePos);

            // Always-on direction indicator -- not gated by selection.
            DrawYawArrows(gl, mvpCopy);

            if (_entitySelectDirty)
            {
                UploadEntitySelectionMesh(gl);
                _entitySelectDirty = false;
            }
            DrawEntitySelection(gl, view, proj);
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GL_DEPTH_TEST);
    }

    unsafe void UploadEntities(GlInterface gl, ScenarioPreviewData data)
    {
        _entityCount = data.Entities.Count;
        if (_entityCount == 0) return;
        if (_glVertexAttribDivisor == null) return;

        // Per instance: pos.xyz + color.rgba + underwater flag.
        // AoMR stores entity X/Z in half-tile units (1 tile = 2 stored units).
        const int floatsPerInstance = 8;
        int total = _entityCount * floatsPerInstance;
        var inst = ArrayPool<float>.Shared.Rent(total);
        try
        {
            float[]? tileWaterY = data.WaterMesh?.TileWaterY;
            int waterMapX = data.WaterMesh?.MapX ?? 0;
            int waterMapZ = data.WaterMesh?.MapZ ?? 0;

            for (int i = 0; i < _entityCount; i++)
            {
                var m = data.Entities[i];
                int o = i * floatsPerInstance;
                var pos = m.Position;
                if (_previewOffset.TryGetValue(m.EntityId, out var off)) pos += off;
                inst[o + 0] = pos.X * 0.5f;
                inst[o + 1] = pos.Y;
                inst[o + 2] = pos.Z * 0.5f;
                var c = CryBar.Scenario.PlayerColors.GetRgb(m.PlayerId);
                inst[o + 3] = c.R; inst[o + 4] = c.G; inst[o + 5] = c.B; inst[o + 6] = 1f;

                float underwater = 0f;
                if (tileWaterY is not null)
                {
                    int tx = (int)(pos.X * 0.5f);
                    int tz = (int)(pos.Z * 0.5f);
                    if ((uint)tx < (uint)waterMapX && (uint)tz < (uint)waterMapZ)
                    {
                        float wy = tileWaterY[tz * waterMapX + tx];
                        if (!float.IsNaN(wy) && pos.Y < wy) underwater = 1f;
                    }
                }
                inst[o + 7] = underwater;
            }

            gl.BindVertexArray(_billboardVao);

            gl.BindBuffer(GL_ARRAY_BUFFER, _billboardQuadVbo);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GL_FLOAT_TYPE, 0, 2 * sizeof(float), IntPtr.Zero);
            _glVertexAttribDivisor(0, 0);

            gl.BindBuffer(GL_ARRAY_BUFFER, _billboardInstanceVbo);
            fixed (float* p = inst)
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(total * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);

            int stride = floatsPerInstance * sizeof(float);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, GL_FLOAT_TYPE, 0, stride, IntPtr.Zero);
            _glVertexAttribDivisor(1, 1);
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 4, GL_FLOAT_TYPE, 0, stride, new IntPtr(3 * sizeof(float)));
            _glVertexAttribDivisor(2, 1);
            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 1, GL_FLOAT_TYPE, 0, stride, new IntPtr(7 * sizeof(float)));
            _glVertexAttribDivisor(3, 1);

            gl.BindVertexArray(0);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(inst);
        }
    }

    // World-space half-radius of the marker disc. Anchored to world units so the
    // disc zooms naturally with terrain (one tile = 1.0 world unit). 0.4 puts the
    // disc at slightly under half a tile -- visible without dominating the cell.
    const float BillboardHalfSizeWorld = 0.4f;

    // Visual scale applied to terrain Y so peaks sit at game-comparable heights.
    // Raw scenario heights render ~2x too tall in our viewport vs the in-game
    // camera projection; 0.5 lines up the example mountain footprint with how
    // the game renders the same data.
    const float HeightScale = 0.5f;

    unsafe void DrawEntities(GlInterface gl, Matrix4x4 view, Matrix4x4 proj, Vector3 eyePos)
    {
        if (_glDrawArraysInstanced == null) return;
        if (_data is null) return;

        // Fade entire-map distance: full alpha out to fadeNear, ramps to ~35%
        // by fadeFar. Tuned against the typical orbit distance: at the default
        // FitToSphere, fadeFar puts the far corner of the diamond near the
        // bottom of the alpha range while keeping the near corner solid.
        float mapSize = MathF.Max(_data.Terrain.MapSizeX, _data.Terrain.MapSizeZ);
        float fadeNear = mapSize * 0.4f;
        float fadeFar  = mapSize * 1.4f;

        gl.Enable(GL_BLEND);
        if (_glBlendFunc != null) _glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        gl.UseProgram(_billboardProgram);
        gl.UniformMatrix4fv(_uBillboardView, 1, false, &view.M11);
        gl.UniformMatrix4fv(_uBillboardProj, 1, false, &proj.M11);
        gl.Uniform1f(_uBillboardSize, BillboardHalfSizeWorld);
        if (_glUniform3f != null)
            _glUniform3f(_uBillboardCamPos, eyePos.X, eyePos.Y, eyePos.Z);
        gl.Uniform1f(_uBillboardFadeNear, fadeNear);
        gl.Uniform1f(_uBillboardFadeFar, fadeFar);
        gl.Uniform1f(_uBillboardYScale, HeightScale);
        gl.BindVertexArray(_billboardVao);
        _glDrawArraysInstanced(GL_TRIANGLES, 0, 6, _entityCount);
        gl.BindVertexArray(0);

        gl.Disable(GL_BLEND);
    }

    unsafe void UploadEntitySelectionMesh(GlInterface gl)
    {
        if (_data is null || _glVertexAttribDivisor == null) { _entitySelectInstanceCount = 0; return; }
        var sel = _data.Selection;
        int count = sel.Entities.Count;
        if (count == 0) { _entitySelectInstanceCount = 0; return; }

        const int floatsPerInstance = 7;
        var inst = ArrayPool<float>.Shared.Rent(count * floatsPerInstance);
        try
        {
            int o = 0;
            var idToIdx = _data.EntityIdToIndex;
            foreach (uint id in sel.Entities)
            {
                if (!idToIdx.TryGetValue(id, out int idx)) continue;
                var m = _data.Entities[idx];
                var pos = m.Position;
                if (_previewOffset.TryGetValue(id, out var off)) pos += off;
                inst[o + 0] = pos.X * 0.5f;
                inst[o + 1] = pos.Y;
                inst[o + 2] = pos.Z * 0.5f;
                inst[o + 3] = _ringColor.R; inst[o + 4] = _ringColor.G; inst[o + 5] = _ringColor.B; inst[o + 6] = _ringColor.A;
                o += floatsPerInstance;
            }
            int instanceCount = o / floatsPerInstance;

            gl.BindVertexArray(_entitySelectVao);

            gl.BindBuffer(GL_ARRAY_BUFFER, _entitySelectQuadVbo);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GL_FLOAT_TYPE, 0, 2 * sizeof(float), IntPtr.Zero);
            _glVertexAttribDivisor(0, 0);

            gl.BindBuffer(GL_ARRAY_BUFFER, _entitySelectInstanceVbo);
            fixed (float* p = inst)
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(o * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);

            int stride = floatsPerInstance * sizeof(float);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, GL_FLOAT_TYPE, 0, stride, IntPtr.Zero);
            _glVertexAttribDivisor(1, 1);
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 4, GL_FLOAT_TYPE, 0, stride, new IntPtr(3 * sizeof(float)));
            _glVertexAttribDivisor(2, 1);

            gl.BindVertexArray(0);
            _entitySelectInstanceCount = instanceCount;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(inst);
        }
    }

    unsafe void UploadYawArrows(GlInterface gl)
    {
        if (_data is null) { _yawArrowVertexCount = 0; return; }
        int count = _data.Entities.Count;
        if (count == 0) { _yawArrowVertexCount = 0; return; }

        // World-space line: 0.8 game units (~0.4 visual) -- reaches the disc edge.
        // Black for high contrast against any player color.
        const float ArrowLengthGame = 0.8f;
        const int floatsPerVertex = 7;
        const int floatsPerLine = floatsPerVertex * 2;
        int total = count * floatsPerLine;
        var verts = ArrayPool<float>.Shared.Rent(total);
        try
        {
            int o = 0;
            for (int i = 0; i < count; i++)
            {
                var m = _data.Entities[i];
                var pos = m.Position;
                if (_previewOffset.TryGetValue(m.EntityId, out var off)) pos += off;
                Matrix3x3 rot = (_rotateMode && _rotateOldYaws.TryGetValue(m.EntityId, out var oldYaw))
                    ? Matrix3x3.FromYawDegrees(oldYaw + _rotatePreviewDelta)
                    : m.Rotation;
                var fwd = rot.Multiply(new Vector3(0, 0, 1));
                float endX = pos.X + fwd.X * ArrowLengthGame;
                float endZ = pos.Z + fwd.Z * ArrowLengthGame;

                verts[o++] = pos.X * 0.5f; verts[o++] = pos.Y; verts[o++] = pos.Z * 0.5f;
                verts[o++] = 0f; verts[o++] = 0f; verts[o++] = 0f; verts[o++] = 1f;
                verts[o++] = endX * 0.5f;  verts[o++] = pos.Y; verts[o++] = endZ * 0.5f;
                verts[o++] = 0f; verts[o++] = 0f; verts[o++] = 0f; verts[o++] = 1f;
            }

            gl.BindVertexArray(_yawArrowVao);
            gl.BindBuffer(GL_ARRAY_BUFFER, _yawArrowVbo);
            fixed (float* p = verts)
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(o * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);

            int stride = floatsPerVertex * sizeof(float);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, GL_FLOAT_TYPE, 0, stride, IntPtr.Zero);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, GL_FLOAT_TYPE, 0, stride, new IntPtr(3 * sizeof(float)));
            gl.BindVertexArray(0);

            _yawArrowVertexCount = o / floatsPerVertex;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(verts);
        }
    }

    unsafe void DrawYawArrows(GlInterface gl, Matrix4x4 mvp)
    {
        if (_yawArrowVertexCount == 0) return;
        gl.UseProgram(_yawArrowProgram);
        gl.UniformMatrix4fv(_uYawArrowMvp, 1, false, &mvp.M11);
        gl.Uniform1f(_uYawArrowYScale, HeightScale);
        if (_glLineWidth != null) _glLineWidth(3.0f);
        gl.BindVertexArray(_yawArrowVao);
        gl.DrawArrays(GL_LINES, 0, _yawArrowVertexCount);
        gl.BindVertexArray(0);
        if (_glLineWidth != null) _glLineWidth(1.0f);
    }

    unsafe void DrawEntitySelection(GlInterface gl, Matrix4x4 view, Matrix4x4 proj)
    {
        if (_entitySelectInstanceCount == 0 || _glDrawArraysInstanced == null) return;

        gl.Enable(GL_BLEND);
        if (_glBlendFunc != null) _glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        // Selection overlay must always be visible -- water/terrain otherwise occlude it.
        gl.Disable(GL_DEPTH_TEST);

        gl.UseProgram(_entitySelectProgram);
        gl.UniformMatrix4fv(_uEntitySelectView, 1, false, &view.M11);
        gl.UniformMatrix4fv(_uEntitySelectProj, 1, false, &proj.M11);
        gl.Uniform1f(_uEntitySelectSize, BillboardHalfSizeWorld);
        gl.Uniform1f(_uEntitySelectYScale, HeightScale);
        gl.BindVertexArray(_entitySelectVao);
        _glDrawArraysInstanced(GL_TRIANGLES, 0, 6, _entitySelectInstanceCount);
        gl.BindVertexArray(0);

        gl.Enable(GL_DEPTH_TEST);
        gl.Disable(GL_BLEND);
    }


    public readonly record struct WorldRayHit(int TileX, int TileZ, int VertexX, int VertexZ, float Height);

    public event Action<WorldRayHit?>? CursorHit;
    WorldRayHit? _lastEmittedCursorHit;
    bool _hasEmittedCursorHit;

    public event Action<PickHit, bool, bool>? LeftClicked;
    public event Action<PickHit, bool>? RightClicked;

    public event Action<string?>? ErrorChanged;

    // Fires when the GL context tears down; hosts must drop cached GL handles here.
    public event Action? GlContextLost;

    public void RaiseError(string? message) => ErrorChanged?.Invoke(message);

    unsafe void DrawWater(GlInterface gl, Matrix4x4 mvpCopy)
    {
        if (_waterIndexCount == 0) return;
        gl.Enable(GL_BLEND);
        if (_glBlendFunc != null) _glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        // No depth writes so submerged billboards depth-test against the seabed,
        // not the surface, and remain visible through the water.
        gl.DepthMask(0);
        gl.UseProgram(_waterProgram);
        gl.UniformMatrix4fv(_uWaterMvp, 1, false, &mvpCopy.M11);
        gl.Uniform1f(_uWaterYScale, HeightScale);
        gl.BindVertexArray(_waterVao);
        gl.DrawElements(GL_TRIANGLES, _waterIndexCount, GL_UNSIGNED_INT_TYPE, IntPtr.Zero);
        gl.DepthMask(1);
        gl.Disable(GL_BLEND);
    }

    unsafe void UploadWaterMesh(GlInterface gl, ScenarioPreviewData data)
    {
        var w = data.WaterMesh!;
        _waterIndexCount = w.IndexCount;
        if (_waterIndexCount == 0) return;

        gl.BindVertexArray(_waterVao);
        gl.BindBuffer(GL_ARRAY_BUFFER, _waterVbo);
        fixed (float* p = w.Vertices)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(w.Vertices.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);

        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _waterEbo);
        fixed (uint* p = w.Indices)
            gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)(w.Indices.Length * sizeof(uint)), (IntPtr)p, GL_DYNAMIC_DRAW);

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GL_FLOAT_TYPE, 0, 3 * sizeof(float), IntPtr.Zero);
        gl.BindVertexArray(0);
    }

    unsafe void UploadTileSelectionMesh(GlInterface gl)
    {
        if (_data is null) { _tileSelectVertexCount = 0; return; }
        var sel = _data.Selection;
        int count = sel.Tiles.Count;
        if (count == 0) { _tileSelectVertexCount = 0; return; }

        int mapX = _data.Terrain.MapSizeX;
        int rowStride = mapX + 1;
        var heights = _data.Terrain.Heights;
        // 4 edges per tile, 2 endpoints per edge, 3 floats per endpoint = 24 floats per tile.
        const int floatsPerTile = 24;
        int total = count * floatsPerTile;
        var verts = ArrayPool<float>.Shared.Rent(total);
        try
        {
            int o = 0;
            foreach (int tileIdx in sel.Tiles)
            {
                int tx = tileIdx % mapX;
                int tz = tileIdx / mapX;
                float h00 = heights[tz       * rowStride + tx    ];
                float h10 = heights[tz       * rowStride + tx + 1];
                float h11 = heights[(tz + 1) * rowStride + tx + 1];
                float h01 = heights[(tz + 1) * rowStride + tx    ];

                verts[o++] = tx;     verts[o++] = h00; verts[o++] = tz;
                verts[o++] = tx + 1; verts[o++] = h10; verts[o++] = tz;
                verts[o++] = tx + 1; verts[o++] = h10; verts[o++] = tz;
                verts[o++] = tx + 1; verts[o++] = h11; verts[o++] = tz + 1;
                verts[o++] = tx + 1; verts[o++] = h11; verts[o++] = tz + 1;
                verts[o++] = tx;     verts[o++] = h01; verts[o++] = tz + 1;
                verts[o++] = tx;     verts[o++] = h01; verts[o++] = tz + 1;
                verts[o++] = tx;     verts[o++] = h00; verts[o++] = tz;
            }

            gl.BindVertexArray(_tileSelectVao);
            gl.BindBuffer(GL_ARRAY_BUFFER, _tileSelectVbo);
            fixed (float* p = verts)
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(total * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, GL_FLOAT_TYPE, 0, 3 * sizeof(float), IntPtr.Zero);
            gl.BindVertexArray(0);

            _tileSelectVertexCount = count * 8;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(verts);
        }
    }

    unsafe void DrawTileSelection(GlInterface gl, Matrix4x4 mvp)
    {
        if (_tileSelectVertexCount == 0) return;
        gl.UseProgram(_tileSelectProgram);
        gl.UniformMatrix4fv(_uTileSelectMvp, 1, false, &mvp.M11);
        gl.Uniform1f(_uTileSelectYScale, HeightScale);
        // Yellow outline.
        if (_glUniform4f != null) _glUniform4f(_uTileSelectColor, 1.0f, 0.82f, 0.30f, 1.0f);
        // Selection overlay must always be visible -- water/billboards otherwise occlude it.
        gl.Disable(GL_DEPTH_TEST);
        gl.BindVertexArray(_tileSelectVao);
        gl.DrawArrays(GL_LINES, 0, _tileSelectVertexCount);
        gl.BindVertexArray(0);
        gl.Enable(GL_DEPTH_TEST);
    }

    unsafe void UploadMesh(GlInterface gl, ScenarioPreviewData data)
    {
        var mesh = data.TerrainMesh;

        gl.BindVertexArray(_heightVao);

        gl.BindBuffer(GL_ARRAY_BUFFER, _heightVbo);
        fixed (float* p = mesh.Vertices)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(mesh.Vertices.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);

        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _heightEbo);
        fixed (uint* p = mesh.Indices)
            gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)(mesh.Indices.Length * sizeof(uint)), (IntPtr)p, GL_DYNAMIC_DRAW);

        int stride = TerrainMesh.VertexStrideBytes;
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GL_FLOAT_TYPE, 0, stride, IntPtr.Zero);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, GL_FLOAT_TYPE, 0, stride, new IntPtr(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, GL_FLOAT_TYPE, 0, stride, new IntPtr(5 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 3, GL_FLOAT_TYPE, 0, stride, new IntPtr(9 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    unsafe void EnsureTextureArrayAllocated(GlInterface gl, ScenarioPreviewData data)
    {
        int slices = Math.Max(1, data.TextureSet.Names.Count);

        gl.BindTexture(GL_TEXTURE_2D_ARRAY, _texArray);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_REPEAT);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_REPEAT);
        // GL_LINEAR (not _MIPMAP_LINEAR): texture array slices are 256x256 already
        // and the heightmap has no minification beyond that on a typical map. Avoids
        // a per-slice glGenerateMipmap during streaming, and dodges the silent
        // sampler-returns-zero failure mode if the mipmap proc isn't available.
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

        if (_glTexImage3D != null)
            _glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, GL_RGBA8, SliceSize, SliceSize, slices, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);

        if (_cachedSlices.Length < slices)
        {
            var grown = new byte[]?[slices];
            Array.Copy(_cachedSlices, grown, _cachedSlices.Length);
            _cachedSlices = grown;
        }

        var placeholder = ArrayPool<byte>.Shared.Rent(SliceBytes);
        try
        {
            for (int i = 0; i < SliceBytes; i += 4)
            {
                placeholder[i + 0] = PlaceholderR;
                placeholder[i + 1] = PlaceholderG;
                placeholder[i + 2] = PlaceholderB;
                placeholder[i + 3] = PlaceholderA;
            }

            if (_glTexSubImage3D != null)
            {
                for (int s = 0; s < slices; s++)
                {
                    var src = _cachedSlices[s] ?? placeholder;
                    fixed (byte* p = src)
                        _glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, s, SliceSize, SliceSize, 1, GL_RGBA, GL_UNSIGNED_BYTE, p);
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(placeholder); }

        _allocatedSlices = slices;
    }

    // Replaces slice `sliceIndex` in the texture array with `rgba` (256x256x4 bytes).
    // Returns a task that completes once the upload has executed on the GL thread.
    // The caller must keep `rgba` alive until the returned task completes.
    public Task UploadSliceAsync(int sliceIndex, ReadOnlyMemory<byte> rgba)
    {
        if (rgba.Length != SliceBytes) return Task.CompletedTask;
        if (sliceIndex < 0) return Task.CompletedTask;

        var tcs = new TaskCompletionSource();
        QueueGlAction(gl =>
        {
            try
            {
                if (!_glInitialized || _texArray == 0 || sliceIndex >= _allocatedSlices)
                {
                    tcs.SetResult();
                    return;
                }
                UploadSliceCore(gl, sliceIndex, rgba.Span);
                tcs.SetResult();
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    unsafe void UploadSliceCore(GlInterface gl, int sliceIndex, ReadOnlySpan<byte> rgba)
    {
        if (_glTexSubImage3D == null) return;

        gl.BindTexture(GL_TEXTURE_2D_ARRAY, _texArray);
        fixed (byte* p = rgba)
            _glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, sliceIndex, SliceSize, SliceSize, 1, GL_RGBA, GL_UNSIGNED_BYTE, p);

        if ((uint)sliceIndex < (uint)_cachedSlices.Length)
        {
            var copy = _cachedSlices[sliceIndex] ??= new byte[SliceBytes];
            rgba.CopyTo(copy);
        }

        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        var pos = e.GetPosition(this);
        _lastPointerPos = pos;

        // Right-click during move-mode cancels the in-progress drag.
        if (_moveMode && props.IsRightButtonPressed)
        {
            CancelMoveMode();
            e.Handled = true;
            return;
        }

        // Left-click during rotate-mode cancels.
        if (_rotateMode && props.IsLeftButtonPressed)
        {
            CancelRotateMode();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            // Press on a SELECTED entity arms the hold timer; do NOT start orbit.
            if (_data is { } d
                && d.Selection.Kind == ScenarioSelectionKind.Entities
                && d.Selection.Entities.Count > 0)
            {
                var hit = ComputePickHit(pos);
                if (hit.EntityId is uint id && d.Selection.Entities.Contains(id))
                {
                    _holdArmed = true;
                    _pressScreenPos = pos;
                    _holdTimer?.Stop();
                    _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoldMs) };
                    _holdTimer.Tick += OnHoldTimerTick;
                    _holdTimer.Start();
                    e.Handled = true;
                    return;
                }
            }

            _leftDragging = true;
            _leftDragMoved = false;
            _leftPressPos = pos;
        }
        if (props.IsRightButtonPressed)
        {
            // Right-press on a selected entity arms the rotate hold timer.
            if (_data is { } d
                && d.Selection.Kind == ScenarioSelectionKind.Entities
                && d.Selection.Entities.Count > 0)
            {
                var hit = ComputePickHit(pos);
                if (hit.EntityId is uint id && d.Selection.Entities.Contains(id))
                {
                    _rotateHoldArmed = true;
                    _rotatePressScreenPos = pos;
                    _rotateHoldTimer?.Stop();
                    _rotateHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoldMs) };
                    _rotateHoldTimer.Tick += OnRotateHoldTimerTick;
                    _rotateHoldTimer.Start();
                    e.Handled = true;
                    return;
                }
            }

            _rightDragging = true;
            _rightDragMoved = false;
            _rightPressPos = pos;
        }
        e.Handled = true;
    }

    void OnHoldTimerTick(object? s, EventArgs e)
    {
        _holdTimer?.Stop();
        _holdTimer = null;
        if (!_holdArmed) return;
        _holdArmed = false;
        EnterMoveMode();
    }

    void OnRotateHoldTimerTick(object? s, EventArgs e)
    {
        _rotateHoldTimer?.Stop();
        _rotateHoldTimer = null;
        if (!_rotateHoldArmed) return;
        _rotateHoldArmed = false;
        EnterRotateMode();
    }

    void EnterRotateMode()
    {
        if (_data is null) return;

        _rotateOldYaws.Clear();
        foreach (var id in _data.Selection.Entities)
        {
            if (_data.EntityIdToIndex.TryGetValue(id, out int idx))
                _rotateOldYaws[id] = _data.Entities[idx].Rotation.ExtractYawDegrees();
        }
        if (_rotateOldYaws.Count == 0) return;

        _rotatePreviewDelta = 0f;
        _rotateMode = true;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast);
        _ringColor = RingColorRotate;
        _entitiesUploaded = false;
        _entitySelectDirty = true;
        RequestNextFrameRendering();
    }

    void EnterMoveMode()
    {
        if (_data is null) return;

        // Re-capture anchor on entry to avoid jumps from cursor drift during the hold.
        var cursor = _lastPointerPos;
        if (!TryRaycastTerrain(cursor, out var anchor)) return;

        _moveAnchorWorld = anchor;
        _moveOldPositions.Clear();
        _previewOffset.Clear();
        foreach (var id in _data.Selection.Entities)
        {
            if (_data.EntityIdToIndex.TryGetValue(id, out int idx))
                _moveOldPositions[id] = _data.Entities[idx].Position;
        }
        if (_moveOldPositions.Count == 0) return;

        _moveMode = true;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
        _ringColor = RingColorMove;
        _entitySelectDirty = true;
        _entitiesUploaded = false;
        RequestNextFrameRendering();
    }

    // Bilinear-sample terrain world-space height at world XZ. Mirrors the
    // height computation inside TryPickTileIdx (tile coords == world XZ for
    // terrain), with HeightScale applied so it lines up with the rendered surface.
    float SampleTerrainHeightWorld(float x, float z)
    {
        if (_data is null) return 0f;
        int mapX = _data.Terrain.MapSizeX;
        int mapZ = _data.Terrain.MapSizeZ;
        var heights = _data.Terrain.Heights;
        int rowStride = mapX + 1;

        float cx = Math.Clamp(x, 0f, mapX - 1e-4f);
        float cz = Math.Clamp(z, 0f, mapZ - 1e-4f);
        int tx = (int)cx;
        int tz = (int)cz;
        float fx = cx - tx;
        float fz = cz - tz;
        float h00 = heights[tz       * rowStride + tx    ];
        float h10 = heights[tz       * rowStride + tx + 1];
        float h11 = heights[(tz + 1) * rowStride + tx + 1];
        float h01 = heights[(tz + 1) * rowStride + tx    ];
        return (h00 * (1 - fx) * (1 - fz) + h10 * fx * (1 - fz)
              + h11 * fx * fz + h01 * (1 - fx) * fz) * HeightScale;
    }

    // Raymarch the heightfield to a world hit point (with HeightScale baked in).
    // Returns true with the hit position; false if the ray misses the map.
    bool TryRaycastTerrain(Avalonia.Point screenPos, out Vector3 hitWorld)
    {
        hitWorld = default;
        if (_data is null) return false;
        if (!TryUnprojectRay(screenPos, out var nearW, out var dir)) return false;

        int mapX = _data.Terrain.MapSizeX;
        int mapZ = _data.Terrain.MapSizeZ;

        float tMin = 0f, tMax = float.MaxValue;
        if (MathF.Abs(dir.X) > 1e-6f)
        {
            float t1 = (0    - nearW.X) / dir.X;
            float t2 = (mapX - nearW.X) / dir.X;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
        }
        else if (nearW.X < 0 || nearW.X > mapX) return false;
        if (MathF.Abs(dir.Z) > 1e-6f)
        {
            float t1 = (0    - nearW.Z) / dir.Z;
            float t2 = (mapZ - nearW.Z) / dir.Z;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
        }
        else if (nearW.Z < 0 || nearW.Z > mapZ) return false;
        if (tMax <= 0 || tMin >= tMax) return false;
        tMin = MathF.Max(tMin, 0f);

        const float StepDt = 0.25f;
        int safety = 4 * (mapX + mapZ) + 8;
        Vector3 prev = nearW + dir * tMin;
        float prevH = SampleTerrainHeightWorld(prev.X, prev.Z);
        for (float t = tMin + StepDt; t < tMax && safety-- > 0; t += StepDt)
        {
            var p = nearW + dir * t;
            if (p.X < 0 || p.X >= mapX || p.Z < 0 || p.Z >= mapZ) break;
            float h = SampleTerrainHeightWorld(p.X, p.Z);
            if (p.Y <= h)
            {
                // Linearly bracket between prev (above) and p (below) for a smoother hit.
                float aboveDelta = prev.Y - prevH;
                float belowDelta = p.Y - h;
                float denom = aboveDelta - belowDelta;
                float frac = MathF.Abs(denom) > 1e-6f ? aboveDelta / denom : 0f;
                hitWorld = Vector3.Lerp(prev, p, MathF.Max(0f, MathF.Min(1f, frac)));
                hitWorld.Y = SampleTerrainHeightWorld(hitWorld.X, hitWorld.Z);
                return true;
            }
            prev = p;
            prevH = h;
        }
        return false;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var props = e.GetCurrentPoint(this).Properties;

        // Released while hold-armed (still within HoldMs) -> click, not move.
        // Falls through to the regular slice-C LeftClicked path so multi-select collapses.
        if (_holdArmed && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
        {
            _holdTimer?.Stop();
            _holdTimer = null;
            _holdArmed = false;
            bool ctrlClick = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0;
            bool shiftClick = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0;
            LeftClicked?.Invoke(ComputePickHit(_pressScreenPos), ctrlClick, shiftClick);
            e.Handled = true;
            return;
        }

        // Release while in move mode -> commit the drag as a single command.
        if (_moveMode && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
        {
            CommitMoveMode();
            e.Handled = true;
            return;
        }

        // Right-release while still hold-armed -> click, not rotate. Falls through
        // to the regular RightClicked path (clear / remove from selection).
        if (_rotateHoldArmed && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right)
        {
            _rotateHoldTimer?.Stop();
            _rotateHoldTimer = null;
            _rotateHoldArmed = false;
            bool ctrlClick = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0;
            RightClicked?.Invoke(ComputePickHit(_rotatePressScreenPos), ctrlClick);
            e.Handled = true;
            return;
        }

        // Right-release in rotate mode -> commit.
        if (_rotateMode && e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right)
        {
            CommitRotateMode();
            e.Handled = true;
            return;
        }

        bool wasLeftDown = _leftDragging;
        bool wasRightDown = _rightDragging;
        if (!props.IsLeftButtonPressed) _leftDragging = false;
        if (!props.IsRightButtonPressed) _rightDragging = false;

        var pos = e.GetPosition(this);
        bool ctrl = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0;

        if (wasLeftDown && !_leftDragMoved)
            LeftClicked?.Invoke(ComputePickHit(pos), ctrl, shift);
        if (wasRightDown && !_rightDragMoved)
            RightClicked?.Invoke(ComputePickHit(pos), ctrl);

        e.Handled = true;
    }

    void CommitMoveMode()
    {
        if (_data is null) { ExitMoveModeVisuals(); return; }

        var ids = new List<uint>(_previewOffset.Count);
        var newPositions = new List<Vector3>(_previewOffset.Count);
        foreach (var (id, off) in _previewOffset)
        {
            if (_data.EntityIdToIndex.TryGetValue(id, out int idx))
            {
                ids.Add(id);
                newPositions.Add(_data.Entities[idx].Position + off);
            }
        }

        IScenarioCommand? cmd = ids.Count > 0
            ? SetEntityPositions.Create(_data.Entities, ids, newPositions)
            : null;

        ExitMoveModeVisuals();
        GestureCommitted?.Invoke(cmd);
    }

    void CancelMoveMode() => ExitMoveModeVisuals();

    void ExitMoveModeVisuals()
    {
        if (_holdTimer is not null) { _holdTimer.Stop(); _holdTimer = null; }
        _moveMode = false;
        _holdArmed = false;
        _previewOffset.Clear();
        _moveOldPositions.Clear();
        Cursor = Avalonia.Input.Cursor.Default;
        _ringColor = RingColorDefault;
        _entitySelectDirty = true;
        _entitiesUploaded = false;
        RequestNextFrameRendering();
    }

    void CommitRotateMode()
    {
        if (_data is null || MathF.Abs(_rotatePreviewDelta) < 0.01f)
        {
            ExitRotateModeVisuals();
            return;
        }

        var ids = new List<uint>(_rotateOldYaws.Count);
        var newRots = new List<Matrix3x3>(_rotateOldYaws.Count);
        foreach (var (id, oldYaw) in _rotateOldYaws)
        {
            if (_data.EntityIdToIndex.ContainsKey(id))
            {
                ids.Add(id);
                newRots.Add(Matrix3x3.FromYawDegrees(oldYaw + _rotatePreviewDelta));
            }
        }

        IScenarioCommand? cmd = ids.Count > 0
            ? SetEntityRotations.Create(_data.Entities, ids, newRots)
            : null;

        ExitRotateModeVisuals();
        GestureCommitted?.Invoke(cmd);
    }

    void CancelRotateMode() => ExitRotateModeVisuals();

    void ExitRotateModeVisuals()
    {
        if (_rotateHoldTimer is not null) { _rotateHoldTimer.Stop(); _rotateHoldTimer = null; }
        _rotateMode = false;
        _rotateHoldArmed = false;
        _rotateOldYaws.Clear();
        _rotatePreviewDelta = 0f;
        Cursor = Avalonia.Input.Cursor.Default;
        _ringColor = RingColorDefault;
        _entitySelectDirty = true;
        _entitiesUploaded = false;
        RequestNextFrameRendering();
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastPointerPos.X);
        float dy = (float)(pos.Y - _lastPointerPos.Y);
        _lastPointerPos = pos;

        // Avalonia doesn't fire OnPointerPressed for chord buttons during capture;
        // poll current state every move so right-during-move (and vice versa) cancels.
        var moveProps = e.GetCurrentPoint(this).Properties;
        if (_moveMode && moveProps.IsRightButtonPressed)
        {
            CancelMoveMode();
            e.Handled = true;
            return;
        }
        if (_rotateMode && moveProps.IsLeftButtonPressed)
        {
            CancelRotateMode();
            e.Handled = true;
            return;
        }

        if (_moveMode && _data is { } d)
        {
            if (TryRaycastTerrain(pos, out var hit))
            {
                // Terrain raycast is visual XZ; entity Position is half-tile units (2x).
                // Convert delta to scenario units before adding to Position; re-project
                // to visual when sampling for Y-snap.
                float dxScen = (hit.X - _moveAnchorWorld.X) * 2f;
                float dzScen = (hit.Z - _moveAnchorWorld.Z) * 2f;
                _previewOffset.Clear();
                foreach (var (id, oldPos) in _moveOldPositions)
                {
                    float visualX = (oldPos.X + dxScen) * 0.5f;
                    float visualZ = (oldPos.Z + dzScen) * 0.5f;
                    float newY = SampleTerrainHeightWorld(visualX, visualZ) / HeightScale;
                    _previewOffset[id] = new Vector3(dxScen, newY - oldPos.Y, dzScen);
                }
                _entitySelectDirty = true;
                _entitiesUploaded = false;
                RequestNextFrameRendering();
            }
            e.Handled = true;
            return;
        }

        if (_rotateMode)
        {
            float dxFromPress = (float)(pos.X - _rotatePressScreenPos.X);
            _rotatePreviewDelta = dxFromPress * RotateDegPerPixel;
            _entitiesUploaded = false;
            _entitySelectDirty = true;
            RequestNextFrameRendering();
            e.Handled = true;
            return;
        }

        // Hold-armed: suppress orbit/pan; tick enters move/rotate mode, early release fires click.
        if (_holdArmed || _rotateHoldArmed)
        {
            e.Handled = true;
            return;
        }

        if (_leftDragging)
        {
            double moved = Math.Abs(pos.X - _leftPressPos.X) + Math.Abs(pos.Y - _leftPressPos.Y);
            if (moved > 4) _leftDragMoved = true;
            _camera.Rotate(-dx * 0.3f, dy * 0.3f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
        else if (_rightDragging)
        {
            double moved = Math.Abs(pos.X - _rightPressPos.X) + Math.Abs(pos.Y - _rightPressPos.Y);
            if (moved > 4) _rightDragMoved = true;
            _camera.PanGround(-dx * 0.003f, dy * 0.003f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
        else
        {
            EmitCursorHit(pos);
        }
    }

    int PickEntityIndex(Avalonia.Point pos)
    {
        if (_data is null || _entityCount == 0) return -1;
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return -1;

        float aspect = (float)(bounds.Width / bounds.Height);
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);

        // Billboards are world-anchored, so the screen radius shrinks with distance.
        // For each entity we project both its center and a +X view-space offset
        // matching the rendered half-radius, then measure the resulting pixel gap.
        int best = -1;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < _entityCount; i++)
        {
            var m = _data.Entities[i];
            // Mirror the half-tile scale, Y scale, and lift used in UploadEntities + the vertex shader.
            var wp = new Vector4(m.Position.X * 0.5f, m.Position.Y * HeightScale + 0.4f, m.Position.Z * 0.5f, 1f);
            var viewPos = Vector4.Transform(wp, view);
            var centerClip = Vector4.Transform(viewPos, proj);
            if (centerClip.W <= 0) continue;
            float ndcX = centerClip.X / centerClip.W;
            float ndcY = centerClip.Y / centerClip.W;
            if (ndcX < -1 || ndcX > 1 || ndcY < -1 || ndcY > 1) continue;

            var edgeView = viewPos; edgeView.X += BillboardHalfSizeWorld;
            var edgeClip = Vector4.Transform(edgeView, proj);
            float ndcEdgeX = edgeClip.X / edgeClip.W;
            float ndcRadius = MathF.Abs(ndcEdgeX - ndcX);

            float screenX = (float)((ndcX * 0.5 + 0.5) * bounds.Width);
            float screenY = (float)((1.0 - (ndcY * 0.5 + 0.5)) * bounds.Height);
            float pxRadius = ndcRadius * (float)bounds.Width * 0.5f;
            float maxPx = pxRadius * 1.2f;
            float maxPxSq = maxPx * maxPx;

            float dx = (float)(screenX - pos.X);
            float dy = (float)(screenY - pos.Y);
            float distSq = dx * dx + dy * dy;
            if (distSq < maxPxSq && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }
        return best;
    }

    // Builds a world-space ray from a window-space pointer position. Returns false
    // when the projection isn't invertible or the bounds are degenerate.
    bool TryUnprojectRay(Avalonia.Point pos, out Vector3 nearW, out Vector3 dir)
    {
        nearW = default; dir = default;
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        // NDC from window-space pointer; Y inverted because Avalonia is top-down.
        float ndcX = (float)(2.0 * pos.X / bounds.Width - 1.0);
        float ndcY = (float)(1.0 - 2.0 * pos.Y / bounds.Height);

        float aspect = (float)(bounds.Width / bounds.Height);
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);
        if (!Matrix4x4.Invert(view * proj, out var invMvp)) return false;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invMvp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invMvp);
        if (nearH.W == 0 || farH.W == 0) return false;
        nearW = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var farW = new Vector3(farH.X / farH.W, farH.Y / farH.W, farH.Z / farH.W);
        dir = Vector3.Normalize(farW - nearW);
        return true;
    }

    void EmitCursorHit(Avalonia.Point pos)
    {
        var sub = CursorHit;
        if (sub is null) return;

        WorldRayHit? next = ComputeCursorHit(pos);
        if (_hasEmittedCursorHit && Nullable.Equals(next, _lastEmittedCursorHit)) return;
        _lastEmittedCursorHit = next;
        _hasEmittedCursorHit = true;
        sub(next);
    }

    WorldRayHit? ComputeCursorHit(Avalonia.Point pos)
    {
        if (_data is null) return null;
        if (!TryUnprojectRay(pos, out var nearW, out var dir)) return null;
        // Plane intersect at y = avg height; cheap proxy for a real heightmap raycast.
        if (MathF.Abs(dir.Y) < 1e-5f) return null;
        float t = (_avgHeight * HeightScale - nearW.Y) / dir.Y;
        if (t < 0) return null;
        var hit = nearW + dir * t;

        int mapX = _data.Terrain.MapSizeX;
        int mapZ = _data.Terrain.MapSizeZ;
        if (hit.X < 0 || hit.X > mapX || hit.Z < 0 || hit.Z > mapZ) return null;

        int tileX = Math.Clamp((int)MathF.Floor(hit.X), 0, mapX - 1);
        int tileZ = Math.Clamp((int)MathF.Floor(hit.Z), 0, mapZ - 1);
        int vertexX = Math.Clamp((int)MathF.Round(hit.X), 0, mapX);
        int vertexZ = Math.Clamp((int)MathF.Round(hit.Z), 0, mapZ);

        int vIdx = vertexZ * (mapX + 1) + vertexX;
        float height = vIdx < _data.Terrain.Heights.Length ? _data.Terrain.Heights[vIdx] : 0f;
        return new WorldRayHit(tileX, tileZ, vertexX, vertexZ, height);
    }

    PickHit ComputePickHit(Avalonia.Point pos)
    {
        int? entityIdx = TryPickEntityIdx(pos);
        int? tileIdx = TryPickTileIdx(pos);
        uint? entityId = null;
        if (entityIdx is int ei && _data is not null)
            entityId = _data.Entities[ei].EntityId;
        return new PickHit(tileIdx, entityId);
    }

    int? TryPickEntityIdx(Avalonia.Point pos)
    {
        if (_data is null || _entityCount == 0) return null;
        int idx = PickEntityIndex(pos);
        return idx < 0 ? null : idx;
    }

    // Screen-rect box-select: a world-axis cuboid doesn't match the visible area
    // when the camera is rotated, so we filter by projected disc center instead.
    public List<uint> PickEntitiesInScreenRectBetween(uint anchorId, uint hitId, float yTolerance)
    {
        var ids = new List<uint>();
        if (_data is null || _entityCount == 0) return ids;
        if (!_data.EntityIdToIndex.TryGetValue(anchorId, out int ai)) return ids;
        if (!_data.EntityIdToIndex.TryGetValue(hitId, out int hi)) return ids;

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return ids;
        float aspect = (float)(bounds.Width / bounds.Height);
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);

        var aPos = _data.Entities[ai].Position;
        var bPos = _data.Entities[hi].Position;
        if (!TryProjectEntityCenter(aPos, view, proj, bounds, out var aScreen, out float aPxRadius)) return ids;
        if (!TryProjectEntityCenter(bPos, view, proj, bounds, out var bScreen, out float bPxRadius)) return ids;

        // Pad the screen rect by one disc radius so discs whose centers sit just
        // outside the rect (but whose visible body overlaps it) are still picked.
        double pad = MathF.Max(aPxRadius, bPxRadius);
        double minSx = Math.Min(aScreen.X, bScreen.X) - pad, maxSx = Math.Max(aScreen.X, bScreen.X) + pad;
        double minSy = Math.Min(aScreen.Y, bScreen.Y) - pad, maxSy = Math.Max(aScreen.Y, bScreen.Y) + pad;
        float minWy = MathF.Min(aPos.Y, bPos.Y) - yTolerance;
        float maxWy = MathF.Max(aPos.Y, bPos.Y) + yTolerance;

        foreach (var e in _data.Entities)
        {
            var p = e.Position;
            if (p.Y < minWy || p.Y > maxWy) continue;
            if (!TryProjectEntityCenter(p, view, proj, bounds, out var sp, out _)) continue;
            if (sp.X < minSx || sp.X > maxSx) continue;
            if (sp.Y < minSy || sp.Y > maxSy) continue;
            ids.Add(e.EntityId);
        }
        return ids;
    }

    // Half-tile XZ + scaled Y + 0.4 lift mirror UploadEntities and the billboard
    // vertex shader so the projected point matches the rendered disc center.
    static bool TryProjectEntityCenter(Vector3 worldPos, Matrix4x4 view, Matrix4x4 proj,
        Avalonia.Rect bounds, out Avalonia.Point screen, out float pxRadius)
    {
        var wp = new Vector4(worldPos.X * 0.5f, worldPos.Y * HeightScale + 0.4f, worldPos.Z * 0.5f, 1f);
        var viewPos = Vector4.Transform(wp, view);
        var clip = Vector4.Transform(viewPos, proj);
        if (clip.W <= 0) { screen = default; pxRadius = 0; return false; }
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        float sx = (float)((ndcX * 0.5 + 0.5) * bounds.Width);
        float sy = (float)((1.0 - (ndcY * 0.5 + 0.5)) * bounds.Height);
        screen = new Avalonia.Point(sx, sy);

        var edgeView = viewPos; edgeView.X += BillboardHalfSizeWorld;
        var edgeClip = Vector4.Transform(edgeView, proj);
        float ndcEdgeX = edgeClip.W != 0 ? edgeClip.X / edgeClip.W : ndcX;
        pxRadius = MathF.Abs(ndcEdgeX - ndcX) * (float)bounds.Width * 0.5f;
        return true;
    }

    int? TryPickTileIdx(Avalonia.Point pos)
    {
        if (_data is null) return null;
        if (!TryUnprojectRay(pos, out var nearW, out var dir)) return null;

        int mapX = _data.Terrain.MapSizeX;
        int mapZ = _data.Terrain.MapSizeZ;
        int rowStride = mapX + 1;
        var heights = _data.Terrain.Heights;

        // Clip ray to the map's XZ bounds so we don't march outside the heightfield.
        float tMin = 0f, tMax = float.MaxValue;
        if (MathF.Abs(dir.X) > 1e-6f)
        {
            float t1 = (0     - nearW.X) / dir.X;
            float t2 = (mapX  - nearW.X) / dir.X;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
        }
        else if (nearW.X < 0 || nearW.X > mapX) return null;
        if (MathF.Abs(dir.Z) > 1e-6f)
        {
            float t1 = (0     - nearW.Z) / dir.Z;
            float t2 = (mapZ  - nearW.Z) / dir.Z;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
        }
        else if (nearW.Z < 0 || nearW.Z > mapZ) return null;
        if (tMax <= 0 || tMin >= tMax) return null;
        tMin = MathF.Max(tMin, 0f);

        // March the ray, sampling bilinearly-interpolated terrain height. The first
        // tile where the ray's Y drops at or below the surface is the hit. This
        // matches what the user clicks on slopes -- the avg-plane fallback used
        // before this was off by a tile or two on tilted edges.
        const float StepDt = 0.25f;
        int safety = 4 * (mapX + mapZ) + 8;
        int? prevTile = null;
        for (float t = tMin; t < tMax && safety-- > 0; t += StepDt)
        {
            var p = nearW + dir * t;
            if (p.X < 0 || p.X >= mapX || p.Z < 0 || p.Z >= mapZ) break;

            int tx = (int)p.X;
            int tz = (int)p.Z;
            int tileIdx = tz * mapX + tx;

            float fx = p.X - tx;
            float fz = p.Z - tz;
            float h00 = heights[tz       * rowStride + tx    ];
            float h10 = heights[tz       * rowStride + tx + 1];
            float h11 = heights[(tz + 1) * rowStride + tx + 1];
            float h01 = heights[(tz + 1) * rowStride + tx    ];
            float th = (h00 * (1 - fx) * (1 - fz) + h10 * fx * (1 - fz)
                      + h11 * fx * fz + h01 * (1 - fx) * fz) * HeightScale;

            if (p.Y <= th) return prevTile ?? tileIdx;
            prevTile = tileIdx;
        }
        return null;
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Larger rate than the TMM viewer: scenario maps are big and the multiplicative
        // step felt too small at close-up to traverse quickly.
        _camera.Zoom((float)e.Delta.Y, rate: 0.25f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    static int CreateProgram(GlInterface gl, string vertexSrc, string fragmentSrc)
        => GlShaderHelpers.CreateProgram(gl, vertexSrc, fragmentSrc);
}
