using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using CryBar.Scenario;
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

    int _billboardProgram;
    int _billboardVao, _billboardQuadVbo, _billboardInstanceVbo;
    int _uBillboardView, _uBillboardProj, _uBillboardSize, _uBillboardCamPos, _uBillboardFadeNear, _uBillboardFadeFar, _uBillboardYScale;
    bool _entitiesUploaded;
    int _entityCount;
    float _avgHeight;

    int _texArray;
    int _allocatedSlices;

    bool _glInitialized;

    bool _leftDragging, _rightDragging;
    bool _leftDragMoved, _rightDragMoved;
    Avalonia.Point _lastPointerPos;
    Avalonia.Point _leftPressPos;
    Avalonia.Point _rightPressPos;

    readonly ConcurrentQueue<Action<GlInterface>> _glActionQueue = new();

    // Function pointers for GL calls Avalonia's GlInterface doesn't expose.
    unsafe delegate* unmanaged<int, int, int, int, int, int, int, int, int, void*, void> _glTexImage3D;
    unsafe delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void*, void> _glTexSubImage3D;
    unsafe delegate* unmanaged<uint, uint, void> _glBlendFunc;
    unsafe delegate* unmanaged<uint, uint, void> _glVertexAttribDivisor;
    unsafe delegate* unmanaged<uint, int, int, int, void> _glDrawArraysInstanced;
    unsafe delegate* unmanaged<int, float, float, float, void> _glUniform3f;
    unsafe delegate* unmanaged<int, float, float, float, float, void> _glUniform4f;

    public void QueueGlAction(Action<GlInterface> action)
    {
        _glActionQueue.Enqueue(action);
        RequestNextFrameRendering();
    }

    public void SetScenario(ScenarioPreviewData? data)
    {
        if (_data is not null)
            _data.Selection.Changed -= OnSelectionChanged;

        _data = data;
        _meshUploaded = false;
        _waterUploaded = false;
        _entitiesUploaded = false;
        _tileSelectDirty = true;
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
            float d = distance(uCamPos, wp);
            vFade = 1.0 - smoothstep(uFadeNear, uFadeFar, d);
        }
        """;

    const string BillboardFragmentShaderBody = """
        in vec2 vUv;
        in vec4 vColor;
        in float vFade;
        out vec4 fragColor;
        void main() {
            float r = length(vUv);
            if (r > 1.0) discard;
            float edge = smoothstep(0.92, 1.0, r);
            vec4 col = mix(vColor, vec4(0.0, 0.0, 0.0, 1.0), edge);
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

        // Allocate before draining the queue so queued slice uploads find the
        // texture array storage ready. Without this, any TexSubImage3D arriving
        // before the first render's TexImage3D writes to undefined storage and
        // is silently lost.
        if (!_meshUploaded)
        {
            UploadMesh(gl, _data);
            EnsureTextureArrayAllocated(gl, _data);
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
                _entitiesUploaded = true;
            }
            if (_entityCount > 0)
                DrawEntities(gl, view, proj, eyePos);
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GL_DEPTH_TEST);
    }

    unsafe void UploadEntities(GlInterface gl, ScenarioPreviewData data)
    {
        _entityCount = data.Entities.Length;
        if (_entityCount == 0) return;
        if (_glVertexAttribDivisor == null) return;

        // Per instance: pos.xyz + color.rgba.
        // AoMR stores entity X/Z in half-tile units (1 tile = 2 stored units).
        const int floatsPerInstance = 7;
        var inst = new float[_entityCount * floatsPerInstance];
        for (int i = 0; i < _entityCount; i++)
        {
            var m = data.Entities[i];
            int o = i * floatsPerInstance;
            inst[o + 0] = m.Position.X * 0.5f;
            inst[o + 1] = m.Position.Y;
            inst[o + 2] = m.Position.Z * 0.5f;
            var (r, g, b, a) = PlayerColor(m.PlayerId);
            inst[o + 3] = r; inst[o + 4] = g; inst[o + 5] = b; inst[o + 6] = a;
        }

        gl.BindVertexArray(_billboardVao);

        gl.BindBuffer(GL_ARRAY_BUFFER, _billboardQuadVbo);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, GL_FLOAT_TYPE, 0, 2 * sizeof(float), IntPtr.Zero);
        _glVertexAttribDivisor(0, 0);

        gl.BindBuffer(GL_ARRAY_BUFFER, _billboardInstanceVbo);
        fixed (float* p = inst)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(inst.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);

        int stride = floatsPerInstance * sizeof(float);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, GL_FLOAT_TYPE, 0, stride, IntPtr.Zero);
        _glVertexAttribDivisor(1, 1);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, GL_FLOAT_TYPE, 0, stride, new IntPtr(3 * sizeof(float)));
        _glVertexAttribDivisor(2, 1);

        gl.BindVertexArray(0);
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

    static (float r, float g, float b, float a) PlayerColor(int playerId) => playerId switch
    {
        0 => (0.50f, 0.50f, 0.50f, 1f),  // Gaia / neutral
        1 => (0.30f, 0.50f, 1.00f, 1f),  // Blue
        2 => (1.00f, 0.20f, 0.20f, 1f),  // Red
        3 => (0.20f, 0.85f, 0.30f, 1f),  // Green
        4 => (1.00f, 0.85f, 0.20f, 1f),  // Yellow
        5 => (0.20f, 0.85f, 0.85f, 1f),  // Cyan
        6 => (0.85f, 0.40f, 0.10f, 1f),  // Orange
        7 => (0.55f, 0.30f, 0.85f, 1f),  // Purple
        _ => (1.00f, 0.40f, 0.85f, 1f),  // Pink (catch-all)
    };

    public readonly record struct WorldRayHit(int TileX, int TileZ, int VertexX, int VertexZ, float Height);

    public event Action<WorldRayHit?>? CursorHit;

    public event Action<PickHit, bool>? LeftClicked;
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
        gl.UseProgram(_waterProgram);
        gl.UniformMatrix4fv(_uWaterMvp, 1, false, &mvpCopy.M11);
        gl.Uniform1f(_uWaterYScale, HeightScale);
        gl.BindVertexArray(_waterVao);
        gl.DrawElements(GL_TRIANGLES, _waterIndexCount, GL_UNSIGNED_INT_TYPE, IntPtr.Zero);
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
        var verts = new float[count * 24];
        int o = 0;
        foreach (int tileIdx in sel.Tiles)
        {
            int tx = tileIdx % mapX;
            int tz = tileIdx / mapX;
            float h00 = heights[tz       * rowStride + tx    ];
            float h10 = heights[tz       * rowStride + tx + 1];
            float h11 = heights[(tz + 1) * rowStride + tx + 1];
            float h01 = heights[(tz + 1) * rowStride + tx    ];

            // Edge 1: (tx, tz) -> (tx+1, tz)
            verts[o++] = tx;     verts[o++] = h00; verts[o++] = tz;
            verts[o++] = tx + 1; verts[o++] = h10; verts[o++] = tz;
            // Edge 2: (tx+1, tz) -> (tx+1, tz+1)
            verts[o++] = tx + 1; verts[o++] = h10; verts[o++] = tz;
            verts[o++] = tx + 1; verts[o++] = h11; verts[o++] = tz + 1;
            // Edge 3: (tx+1, tz+1) -> (tx, tz+1)
            verts[o++] = tx + 1; verts[o++] = h11; verts[o++] = tz + 1;
            verts[o++] = tx;     verts[o++] = h01; verts[o++] = tz + 1;
            // Edge 4: (tx, tz+1) -> (tx, tz)
            verts[o++] = tx;     verts[o++] = h01; verts[o++] = tz + 1;
            verts[o++] = tx;     verts[o++] = h00; verts[o++] = tz;
        }

        gl.BindVertexArray(_tileSelectVao);
        gl.BindBuffer(GL_ARRAY_BUFFER, _tileSelectVbo);
        fixed (float* p = verts)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(verts.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GL_FLOAT_TYPE, 0, 3 * sizeof(float), IntPtr.Zero);
        gl.BindVertexArray(0);

        _tileSelectVertexCount = count * 8;
    }

    unsafe void DrawTileSelection(GlInterface gl, Matrix4x4 mvp)
    {
        if (_tileSelectVertexCount == 0) return;
        gl.UseProgram(_tileSelectProgram);
        gl.UniformMatrix4fv(_uTileSelectMvp, 1, false, &mvp.M11);
        gl.Uniform1f(_uTileSelectYScale, HeightScale);
        // Yellow outline.
        if (_glUniform4f != null) _glUniform4f(_uTileSelectColor, 1.0f, 0.82f, 0.30f, 1.0f);
        gl.BindVertexArray(_tileSelectVao);
        gl.DrawArrays(GL_LINES, 0, _tileSelectVertexCount);
        gl.BindVertexArray(0);
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

        // Fill every slice with the placeholder so the heightmap is visible
        // before ScenarioTextureLoader has streamed any real DDTs.
        if (_glTexImage3D != null)
            _glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, GL_RGBA8, SliceSize, SliceSize, slices, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);

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
                fixed (byte* p = placeholder)
                {
                    for (int s = 0; s < slices; s++)
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

        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _lastPointerPos = e.GetPosition(this);
        if (props.IsLeftButtonPressed)
        {
            _leftDragging = true;
            _leftDragMoved = false;
            _leftPressPos = _lastPointerPos;
        }
        if (props.IsRightButtonPressed)
        {
            _rightDragging = true;
            _rightDragMoved = false;
            _rightPressPos = _lastPointerPos;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var props = e.GetCurrentPoint(this).Properties;
        bool wasLeftDown = _leftDragging;
        bool wasRightDown = _rightDragging;
        if (!props.IsLeftButtonPressed) _leftDragging = false;
        if (!props.IsRightButtonPressed) _rightDragging = false;

        var pos = e.GetPosition(this);
        bool ctrl = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0;

        if (wasLeftDown && !_leftDragMoved)
            LeftClicked?.Invoke(ComputePickHit(pos), ctrl);
        if (wasRightDown && !_rightDragMoved)
            RightClicked?.Invoke(ComputePickHit(pos), ctrl);

        e.Handled = true;
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastPointerPos.X);
        float dy = (float)(pos.Y - _lastPointerPos.Y);
        _lastPointerPos = pos;

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

    void EmitCursorHit(Avalonia.Point pos)
    {
        var sub = CursorHit;
        if (sub is null) return;
        if (_data is null) { sub(null); return; }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) { sub(null); return; }

        // NDC from window-space pointer; Y inverted because Avalonia is top-down.
        float ndcX = (float)(2.0 * pos.X / bounds.Width - 1.0);
        float ndcY = (float)(1.0 - 2.0 * pos.Y / bounds.Height);

        float aspect = (float)(bounds.Width / bounds.Height);
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);
        var mvp = view * proj;
        if (!Matrix4x4.Invert(mvp, out var invMvp)) { sub(null); return; }

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invMvp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invMvp);
        if (nearH.W == 0 || farH.W == 0) { sub(null); return; }
        var nearW = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var farW  = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir = Vector3.Normalize(farW - nearW);

        // Plane intersect at y = avg height; cheap proxy for a real heightmap raycast.
        // The rendered terrain is scaled by HeightScale, so the plane Y has to match.
        if (MathF.Abs(dir.Y) < 1e-5f) { sub(null); return; }
        float t = (_avgHeight * HeightScale - nearW.Y) / dir.Y;
        if (t < 0) { sub(null); return; }
        var hit = nearW + dir * t;

        int mapX = _data.Terrain.MapSizeX;
        int mapZ = _data.Terrain.MapSizeZ;
        if (hit.X < 0 || hit.X > mapX || hit.Z < 0 || hit.Z > mapZ) { sub(null); return; }

        int tileX = Math.Clamp((int)MathF.Floor(hit.X), 0, mapX - 1);
        int tileZ = Math.Clamp((int)MathF.Floor(hit.Z), 0, mapZ - 1);
        int vertexX = Math.Clamp((int)MathF.Round(hit.X), 0, mapX);
        int vertexZ = Math.Clamp((int)MathF.Round(hit.Z), 0, mapZ);

        int vIdx = vertexZ * (mapX + 1) + vertexX;
        float height = vIdx < _data.Terrain.Heights.Length ? _data.Terrain.Heights[vIdx] : 0f;

        sub(new WorldRayHit(tileX, tileZ, vertexX, vertexZ, height));
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

    int? TryPickTileIdx(Avalonia.Point pos)
    {
        if (_data is null) return null;
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        float ndcX = (float)(2.0 * pos.X / bounds.Width - 1.0);
        float ndcY = (float)(1.0 - 2.0 * pos.Y / bounds.Height);
        float aspect = (float)(bounds.Width / bounds.Height);
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);
        var mvp = view * proj;
        if (!Matrix4x4.Invert(mvp, out var invMvp)) return null;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invMvp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invMvp);
        if (nearH.W == 0 || farH.W == 0) return null;
        var nearW = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var farW  = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir = Vector3.Normalize(farW - nearW);
        if (MathF.Abs(dir.Y) < 1e-5f) return null;
        float t = (_avgHeight * HeightScale - nearW.Y) / dir.Y;
        if (t < 0) return null;
        var hit = nearW + dir * t;

        int mapX = _data.Terrain.MapSizeX;
        int mapZ = _data.Terrain.MapSizeZ;
        if (hit.X < 0 || hit.X > mapX || hit.Z < 0 || hit.Z > mapZ) return null;

        int tx = Math.Clamp((int)hit.X, 0, mapX - 1);
        int tz = Math.Clamp((int)hit.Z, 0, mapZ - 1);
        return tz * mapX + tx;
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
