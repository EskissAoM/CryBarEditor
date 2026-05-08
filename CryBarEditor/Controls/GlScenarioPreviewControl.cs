using System;
using System.Collections.Concurrent;
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
    const int GL_TRIANGLES            = 0x0004;
    const int GL_LESS                 = 0x0201;
    const int GL_DEPTH_COMPONENT      = 0x1902;
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

    // Grass-green placeholder color until real terrain textures upload.
    // Matches the user's "default tile is green grass" guidance.
    const byte PlaceholderR = 0x4E, PlaceholderG = 0x6B, PlaceholderB = 0x33, PlaceholderA = 0xFF;

    const int SliceSize = 256;
    const int SliceBytes = SliceSize * SliceSize * 4;

    readonly OrbitCamera _camera = new();
    ScenarioPreviewData? _data;
    bool _meshUploaded;

    int _heightProgram;
    int _heightVao, _heightVbo, _heightEbo;
    int _uMvp, _uTexArray;

    int _waterProgram;
    int _waterVao, _waterVbo;
    int _uWaterMvp;
    bool _waterUploaded;

    int _billboardProgram;
    int _billboardVao, _billboardQuadVbo, _billboardInstanceVbo;
    int _uBillboardMvp, _uBillboardSize;
    bool _entitiesUploaded;
    int _entityCount;
    float _avgHeight;

    int _texArray;
    int _allocatedSlices;

    bool _glInitialized;

    bool _leftDragging, _rightDragging;
    Avalonia.Point _lastPointerPos;

    readonly ConcurrentQueue<Action<GlInterface>> _glActionQueue = new();

    // Function pointers for GL calls Avalonia's GlInterface doesn't expose.
    // Loaded once in OnOpenGlInit, like the pattern in GlPreviewControl.
    unsafe delegate* unmanaged<int, int, int, int, int, int, int, int, int, void*, void> _glTexImage3D;
    unsafe delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void*, void> _glTexSubImage3D;
    unsafe delegate* unmanaged<int, void> _glGenerateMipmap;
    unsafe delegate* unmanaged<uint, uint, void> _glBlendFunc;
    unsafe delegate* unmanaged<uint, uint, void> _glVertexAttribDivisor;
    unsafe delegate* unmanaged<uint, int, int, int, void> _glDrawArraysInstanced;

    public void QueueGlAction(Action<GlInterface> action)
    {
        _glActionQueue.Enqueue(action);
        RequestNextFrameRendering();
    }

    public void SetScenario(ScenarioPreviewData? data)
    {
        _data = data;
        _meshUploaded = false;
        _waterUploaded = false;
        _entitiesUploaded = false;
        if (data is not null)
        {
            float cx = data.Terrain.MapSizeX * 0.5f;
            float cz = data.Terrain.MapSizeZ * 0.5f;
            float radius = MathF.Max(data.Terrain.MapSizeX, data.Terrain.MapSizeZ) * 0.55f;
            _camera.FitToSphere(cx, 0f, cz, radius);

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

    const string VertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aSlices;
        layout(location = 3) in vec3 aWeights;

        uniform mat4 uMVP;

        out vec3 vWorld;
        out vec4 vSlices;
        out vec4 vWeights;

        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vWorld = aPos;
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
        uniform mat4 uMVP;
        uniform float uSize;
        out vec2 vUv;
        out vec4 vColor;
        void main() {
            vec4 clip = uMVP * vec4(aWorldPos, 1.0);
            // Pixel-stable size: aQuad is in [-1,1], scaled by uSize in clip space
            clip.xy += aQuad * uSize * clip.w;
            gl_Position = clip;
            vUv = aQuad;
            vColor = aColor;
        }
        """;

    const string BillboardFragmentShaderBody = """
        in vec2 vUv;
        in vec4 vColor;
        out vec4 fragColor;
        void main() {
            float r = length(vUv);
            if (r > 1.0) discard;
            float edge = smoothstep(0.92, 1.0, r);
            fragColor = mix(vColor, vec4(0.0, 0.0, 0.0, 1.0), edge);
        }
        """;

    const string WaterVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMVP;
        void main() { gl_Position = uMVP * vec4(aPos, 1.0); }
        """;

    const string WaterFragmentShaderBody = """
        out vec4 fragColor;
        void main() { fragColor = vec4(0.20, 0.40, 0.55, 0.55); }
        """;

    const string FragmentShaderBody = """
        in vec3 vWorld;
        in vec4 vSlices;
        in vec4 vWeights;

        uniform sampler2DArray uTexArray;

        out vec4 fragColor;

        void main() {
            // Tile-local UV from world XZ. fract gives [0,1) inside each tile.
            vec2 uv = fract(vWorld.xz);

            // Slice indices are linearly interpolated across the triangle. Round to
            // nearest integer so each fragment samples whole slices (proper bilinear
            // blending across tile boundaries needs flat-shaded slices + 4-vert
            // unshared mesh, scheduled as a follow-up).
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

            // Fixed light from the upper-right so peaks read against valleys
            vec3 N = vec3(0.0, 1.0, 0.0);
            float NdotL = max(dot(N, normalize(vec3(0.4, 1.0, 0.3))), 0.0);
            fragColor = vec4(col.rgb * (0.55 + 0.45 * NdotL), 1.0);
        }
        """;

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        bool isGles = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
        string vsPreamble = isGles ? "#version 300 es\n" : "#version 330 core\n";
        string fsPreamble = isGles ? "#version 300 es\nprecision mediump float;\n" : "#version 330 core\n";

        _glTexImage3D    = (delegate* unmanaged<int, int, int, int, int, int, int, int, int, void*, void>)gl.GetProcAddress("glTexImage3D");
        _glTexSubImage3D = (delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void*, void>)gl.GetProcAddress("glTexSubImage3D");
        _glGenerateMipmap = (delegate* unmanaged<int, void>)gl.GetProcAddress("glGenerateMipmap");
        _glBlendFunc = (delegate* unmanaged<uint, uint, void>)gl.GetProcAddress("glBlendFunc");
        _glVertexAttribDivisor = (delegate* unmanaged<uint, uint, void>)gl.GetProcAddress("glVertexAttribDivisor");
        _glDrawArraysInstanced = (delegate* unmanaged<uint, int, int, int, void>)gl.GetProcAddress("glDrawArraysInstanced");

        _heightProgram = CreateProgram(gl, vsPreamble + VertexShaderBody, fsPreamble + FragmentShaderBody);
        _uMvp      = gl.GetUniformLocationString(_heightProgram, "uMVP");
        _uTexArray = gl.GetUniformLocationString(_heightProgram, "uTexArray");

        _heightVao = gl.GenVertexArray();
        _heightVbo = gl.GenBuffer();
        _heightEbo = gl.GenBuffer();

        _texArray = gl.GenTexture();

        _waterProgram = CreateProgram(gl, vsPreamble + WaterVertexShaderBody, fsPreamble + WaterFragmentShaderBody);
        _uWaterMvp = gl.GetUniformLocationString(_waterProgram, "uMVP");
        _waterVao = gl.GenVertexArray();
        _waterVbo = gl.GenBuffer();

        _billboardProgram = CreateProgram(gl, vsPreamble + BillboardVertexShaderBody, fsPreamble + BillboardFragmentShaderBody);
        _uBillboardMvp = gl.GetUniformLocationString(_billboardProgram, "uMVP");
        _uBillboardSize = gl.GetUniformLocationString(_billboardProgram, "uSize");
        _billboardVao = gl.GenVertexArray();
        _billboardQuadVbo = gl.GenBuffer();
        _billboardInstanceVbo = gl.GenBuffer();
        InitBillboardQuad(gl);

        _glInitialized = true;
    }

    unsafe void InitBillboardQuad(GlInterface gl)
    {
        // 6 vertices, two CCW triangles, each vertex = (qx, qy) in [-1,1]
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
        if (_heightProgram != 0) { gl.DeleteProgram(_heightProgram); _heightProgram = 0; }
        if (_heightVbo != 0)     { gl.DeleteBuffer(_heightVbo); _heightVbo = 0; }
        if (_heightEbo != 0)     { gl.DeleteBuffer(_heightEbo); _heightEbo = 0; }
        if (_heightVao != 0)     { gl.DeleteVertexArray(_heightVao); _heightVao = 0; }
        if (_texArray != 0)      { gl.DeleteTexture(_texArray); _texArray = 0; }
        if (_waterProgram != 0)  { gl.DeleteProgram(_waterProgram); _waterProgram = 0; }
        if (_waterVbo != 0)      { gl.DeleteBuffer(_waterVbo); _waterVbo = 0; }
        if (_waterVao != 0)      { gl.DeleteVertexArray(_waterVao); _waterVao = 0; }
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
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        while (_glActionQueue.TryDequeue(out var pending))
            pending(gl);

        gl.BindFramebuffer(GL_FRAMEBUFFER, fb);

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = (int)(Bounds.Width * scaling);
        int h = (int)(Bounds.Height * scaling);
        if (w <= 0 || h <= 0) return;

        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0.05f, 0.06f, 0.08f, 1.0f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        if (_data is null || !_glInitialized) return;

        if (!_meshUploaded)
        {
            UploadMesh(gl, _data);
            EnsureTextureArrayAllocated(gl, _data);
            _meshUploaded = true;
        }

        gl.Enable(GL_DEPTH_TEST);

        float aspect = (float)w / h;
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(aspect);
        var mvp = view * proj;
        var mvpCopy = mvp;

        gl.UseProgram(_heightProgram);
        gl.UniformMatrix4fv(_uMvp, 1, false, &mvpCopy.M11);

        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D_ARRAY, _texArray);
        gl.Uniform1i(_uTexArray, 0);

        gl.BindVertexArray(_heightVao);
        gl.DrawElements(GL_TRIANGLES, _data.TerrainMesh.Indices.Length, GL_UNSIGNED_INT_TYPE, IntPtr.Zero);

        if (_data.WaterMesh is not null)
        {
            if (!_waterUploaded)
            {
                UploadWaterMesh(gl, _data);
                _waterUploaded = true;
            }
            DrawWater(gl, mvpCopy);
        }

        if (!_entitiesUploaded)
        {
            UploadEntities(gl, _data);
            _entitiesUploaded = true;
        }
        if (_entityCount > 0)
            DrawEntities(gl, mvpCopy, MathF.Max(_data.Terrain.MapSizeX, _data.Terrain.MapSizeZ));

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GL_DEPTH_TEST);
    }

    unsafe void UploadEntities(GlInterface gl, ScenarioPreviewData data)
    {
        _entityCount = data.Entities.Length;
        if (_entityCount == 0) return;
        if (_glVertexAttribDivisor == null) return;

        // Per-instance: 3 floats world pos + 4 floats RGBA color = 7 floats per entity
        const int floatsPerInstance = 7;
        var inst = new float[_entityCount * floatsPerInstance];
        for (int i = 0; i < _entityCount; i++)
        {
            var m = data.Entities[i];
            int o = i * floatsPerInstance;
            // Game stores entity position as (x, y_height, z); pass through unchanged.
            inst[o + 0] = m.Position.X;
            inst[o + 1] = m.Position.Y;
            inst[o + 2] = m.Position.Z;
            var (r, g, b, a) = PlayerColor(m.PlayerId);
            inst[o + 3] = r; inst[o + 4] = g; inst[o + 5] = b; inst[o + 6] = a;
        }

        gl.BindVertexArray(_billboardVao);

        // Reuse the static quad VBO bound by InitBillboardQuad on attrib 0.
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

    unsafe void DrawEntities(GlInterface gl, Matrix4x4 mvpCopy, float mapExtent)
    {
        if (_glDrawArraysInstanced == null) return;
        gl.UseProgram(_billboardProgram);
        gl.UniformMatrix4fv(_uBillboardMvp, 1, false, &mvpCopy.M11);
        // Marker radius scales with map extent so it stays visible from default zoom.
        gl.Uniform1f(_uBillboardSize, mapExtent * 0.005f);
        gl.BindVertexArray(_billboardVao);
        _glDrawArraysInstanced(GL_TRIANGLES, 0, 6, _entityCount);
        gl.BindVertexArray(0);
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

    public readonly record struct LoadProgress(int Resolved, int Decoded, int Uploaded, int Total);

    public event Action<LoadProgress>? LoadProgressChanged;
    public event Action<string?>? ErrorChanged;

    public void RaiseLoadProgress(int resolved, int decoded, int uploaded, int total)
        => LoadProgressChanged?.Invoke(new LoadProgress(resolved, decoded, uploaded, total));

    public void RaiseError(string? message) => ErrorChanged?.Invoke(message);

    unsafe void DrawWater(GlInterface gl, Matrix4x4 mvpCopy)
    {
        gl.Enable(GL_BLEND);
        if (_glBlendFunc != null) _glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        gl.UseProgram(_waterProgram);
        gl.UniformMatrix4fv(_uWaterMvp, 1, false, &mvpCopy.M11);
        gl.BindVertexArray(_waterVao);
        gl.DrawArrays(GL_TRIANGLES, 0, 6);
        gl.Disable(GL_BLEND);
    }

    unsafe void UploadWaterMesh(GlInterface gl, ScenarioPreviewData data)
    {
        var w = data.WaterMesh!;
        // Two CCW triangles covering the map extent at the median water height.
        var verts = new float[]
        {
            0,           w.Height, 0,
            w.MapSizeX,  w.Height, 0,
            0,           w.Height, w.MapSizeZ,
            w.MapSizeX,  w.Height, 0,
            w.MapSizeX,  w.Height, w.MapSizeZ,
            0,           w.Height, w.MapSizeZ,
        };
        gl.BindVertexArray(_waterVao);
        gl.BindBuffer(GL_ARRAY_BUFFER, _waterVbo);
        fixed (float* p = verts)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(verts.Length * sizeof(float)), (IntPtr)p, GL_DYNAMIC_DRAW);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GL_FLOAT_TYPE, 0, 3 * sizeof(float), IntPtr.Zero);
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

        data.VertexBuffer = _heightVbo;
        data.IndexBuffer = _heightEbo;
        data.VertexArray = _heightVao;
    }

    unsafe void EnsureTextureArrayAllocated(GlInterface gl, ScenarioPreviewData data)
    {
        int slices = Math.Max(1, data.TextureSet.Names.Count);

        gl.BindTexture(GL_TEXTURE_2D_ARRAY, _texArray);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_REPEAT);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_REPEAT);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

        // Fill every slice with grass-green so the heightmap is visible immediately;
        // real textures will replace slices via UploadSliceAsync (Task 15).
        if (_glTexImage3D != null)
            _glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, GL_RGBA8, SliceSize, SliceSize, slices, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);

        var placeholder = new byte[SliceBytes];
        for (int i = 0; i < placeholder.Length; i += 4)
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

        if (_glGenerateMipmap != null)
            _glGenerateMipmap(GL_TEXTURE_2D_ARRAY);

        _allocatedSlices = slices;
        data.TextureArray = _texArray;
    }

    // Replaces slice `sliceIndex` in the texture array with `rgba` (256x256x4 bytes).
    // Returns a task that completes once the upload has executed on the GL thread.
    // Per-call mipmap regeneration is acceptable here -- typical scenarios have
    // ~20 slices, so the overhead is bounded; switching to a single batched
    // regenerate-on-completion pass is a follow-up if profiling demands it.
    public Task UploadSliceAsync(int sliceIndex, byte[] rgba)
    {
        if (rgba is null || rgba.Length != SliceBytes)
            return Task.CompletedTask;
        if (sliceIndex < 0)
            return Task.CompletedTask;

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
                UploadSliceCore(gl, sliceIndex, rgba);
                tcs.SetResult();
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    unsafe void UploadSliceCore(GlInterface gl, int sliceIndex, byte[] rgba)
    {
        if (_glTexSubImage3D == null) return;

        gl.BindTexture(GL_TEXTURE_2D_ARRAY, _texArray);
        fixed (byte* p = rgba)
            _glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, sliceIndex, SliceSize, SliceSize, 1, GL_RGBA, GL_UNSIGNED_BYTE, p);

        if (_glGenerateMipmap != null)
            _glGenerateMipmap(GL_TEXTURE_2D_ARRAY);

        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _lastPointerPos = e.GetPosition(this);
        if (props.IsLeftButtonPressed) _leftDragging = true;
        if (props.IsRightButtonPressed) _rightDragging = true;
        e.Handled = true;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) _leftDragging = false;
        if (!props.IsRightButtonPressed) _rightDragging = false;
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
            _camera.Rotate(-dx * 0.3f, dy * 0.3f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
        else if (_rightDragging)
        {
            _camera.Pan(-dx * 0.003f, dy * 0.003f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
        else
        {
            EmitCursorHit(pos);
        }
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

        // Plane intersect at y = avg height -- accurate enough for an HUD readout
        // until per-vertex heightmap raycast is added.
        if (MathF.Abs(dir.Y) < 1e-5f) { sub(null); return; }
        float t = (_avgHeight - nearW.Y) / dir.Y;
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

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Zoom((float)e.Delta.Y);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    static int CreateProgram(GlInterface gl, string vertexSrc, string fragmentSrc)
    {
        int vs = CompileShader(gl, GL_VERTEX_SHADER, vertexSrc);
        int fs = CompileShader(gl, GL_FRAGMENT_SHADER, fragmentSrc);
        int program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }

    static int CompileShader(GlInterface gl, int type, string source)
    {
        int shader = gl.CreateShader(type);
        gl.ShaderSourceString(shader, source);
        gl.CompileShader(shader);
        return shader;
    }
}
