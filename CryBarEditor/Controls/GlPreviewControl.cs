using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using CryBarEditor.Classes;
using static Avalonia.OpenGL.GlConsts;

namespace CryBarEditor.Controls;

public class GlPreviewControl : OpenGlControlBase, ICustomHitTest
{
    // OpenGlControlBase has no background, so implement ICustomHitTest for pointer events
    bool ICustomHitTest.HitTest(Point point) => Bounds.Contains(point);

    readonly OrbitCamera _camera = new();
    PreviewMeshData? _meshData;
    bool _meshDirty;

    // GL resources
    int _program;
    int _vao, _vbo, _ebo;
    int _uMvp, _uLightDir, _uColor;
    bool _glInitialized;

    // Public API: gizmo label projection (consumed by overlay canvas)
    public readonly record struct GizmoLabel(int Axis, string Letter, double X, double Y, bool Hovered);

    public event Action<IReadOnlyList<GizmoLabel>>? GizmoLabelsProjected;
    readonly List<GizmoLabel> _gizmoLabelBuffer = new(3);

    // Public API: marker label projection (consumed by overlay canvas)
    public readonly record struct MarkerLabel(string Name, double X, double Y, bool Visible);

    public event Action<IReadOnlyList<MarkerLabel>>? MarkersProjected;
    readonly List<MarkerLabel> _markerLabelBuffer = new();

    // Gizmo GL resources
    int _gizmoProgram;
    int _gizmoVao, _gizmoVbo;
    int _gizmoVertexCount;
    int _uGizmoView, _uGizmoColor;
    const int GizmoSizePx = 96;
    const int GizmoMarginPx = 8;

    // Markers GL resources
    int _markersProgram;
    int _markersVao, _markersVbo;
    int _uMarkersMvp, _uMarkersColor;
    int _markersVertexCapacity;

    bool _showMarkers = true;
    public bool ShowMarkers
    {
        get => _showMarkers;
        set
        {
            if (_showMarkers == value) return;
            _showMarkers = value;
            RequestNextFrameRendering();
        }
    }

    // Ground grid GL resources
    int _gridProgram;
    int _gridVao, _gridVbo;
    int _uGridMvp, _uGridColor;
    int _gridVertexCount;

    bool _showGroundGrid = true;
    public bool ShowGroundGrid
    {
        get => _showGroundGrid;
        set
        {
            if (_showGroundGrid == value) return;
            _showGroundGrid = value;
            RequestNextFrameRendering();
        }
    }

    // Gizmo hover / animation state
    int _hoveredGizmoAxis = -1;
    bool _animActive;
    float _animStartAz, _animStartEl, _animEndAz, _animEndEl;
    double _animStartTimeMs;
    const double AnimDurationMs = 200.0;
    readonly System.Diagnostics.Stopwatch _animClock = new();

    // Function pointer for glUniform3f (not exposed by Avalonia's GlInterface)
    unsafe delegate* unmanaged<int, float, float, float, void> _glUniform3f;
    // Function pointer for glUniform4f (not exposed by Avalonia's GlInterface)
    unsafe delegate* unmanaged<int, float, float, float, float, void> _glUniform4f;
    // Function pointer for glUniformMatrix3fv (not exposed by Avalonia's GlInterface)
    unsafe delegate* unmanaged<int, int, byte, float*, void> _glUniformMatrix3fv;
    // Function pointer for glLineWidth (not exposed by Avalonia's GlInterface)
    unsafe delegate* unmanaged<float, void> _glLineWidth;

    // Mouse tracking
    Point _lastPointerPos;
    bool _leftDragging, _rightDragging;

    // Shader bodies - version/precision preamble prepended at runtime
    const string VertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        uniform mat4 uMVP;
        out vec3 vNormal;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vNormal = normalize(aNormal);
        }
        """;

    const string FragmentShaderBody = """
        in vec3 vNormal;
        uniform vec3 uLightDir;
        uniform vec3 uColor;
        out vec4 FragColor;
        void main() {
            float diff = max(dot(normalize(vNormal), uLightDir), 0.0);
            vec3 col = uColor * (0.25 + 0.75 * diff);
            FragColor = vec4(col, 1.0);
        }
        """;

    const string MarkersVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMVP;
        void main() { gl_Position = uMVP * vec4(aPos, 1.0); }
        """;

    const string MarkersFragmentShaderBody = """
        uniform vec3 uColor;
        out vec4 FragColor;
        void main() { FragColor = vec4(uColor, 1.0); }
        """;

    const string GridVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMVP;
        void main() { gl_Position = uMVP * vec4(aPos, 1.0); }
        """;

    const string GridFragmentShaderBody = """
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() { FragColor = uColor; }
        """;

    const string GizmoVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        uniform mat3 uViewRot;
        void main() {
            vec3 v = uViewRot * aPos;
            // Orthographic projection into clip space; gizmo viewport is square
            gl_Position = vec4(v.x, v.y, -v.z * 0.001, 1.0);
        }
        """;

    const string GizmoFragmentShaderBody = """
        uniform vec3 uColor;
        out vec4 FragColor;
        void main() {
            FragColor = vec4(uColor, 1.0);
        }
        """;

    public void LoadMesh(PreviewMeshData meshData, bool resetCamera = false)
    {
        _meshDirty = true;
        if (_meshData == null || resetCamera)
            _camera.FitToSphere(meshData.CenterX, meshData.CenterY, meshData.CenterZ, meshData.Radius);
        _meshData = meshData;
        RequestNextFrameRendering();
    }

    public void ClearMesh()
    {
        _meshData = null;
        _meshDirty = true;
        RequestNextFrameRendering();
    }

    public void ResetCamera()
    {
        if (_meshData != null)
            _camera.FitToSphere(_meshData.CenterX, _meshData.CenterY, _meshData.CenterZ, _meshData.Radius);
        RequestNextFrameRendering();
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        bool isGles = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
        string vsPreamble = isGles ? "#version 300 es\n" : "#version 330 core\n";
        string fsPreamble = isGles ? "#version 300 es\nprecision mediump float;\n" : "#version 330 core\n";

        // Get proc address for glUniform3f (not in Avalonia's GlInterface)
        _glUniform3f = (delegate* unmanaged<int, float, float, float, void>)gl.GetProcAddress("glUniform3f");
        _glUniform4f = (delegate* unmanaged<int, float, float, float, float, void>)gl.GetProcAddress("glUniform4f");
        _glUniformMatrix3fv = (delegate* unmanaged<int, int, byte, float*, void>)gl.GetProcAddress("glUniformMatrix3fv");
        _glLineWidth = (delegate* unmanaged<float, void>)gl.GetProcAddress("glLineWidth");

        _program = CreateProgram(gl, vsPreamble + VertexShaderBody, fsPreamble + FragmentShaderBody);
        _uMvp = gl.GetUniformLocationString(_program, "uMVP");
        _uLightDir = gl.GetUniformLocationString(_program, "uLightDir");
        _uColor = gl.GetUniformLocationString(_program, "uColor");

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();

        gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo);

        // layout 0 = pos (3 floats), offset 0, stride 48
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 48, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        // layout 1 = normal (3 floats), offset 12, stride 48
        gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, 48, new IntPtr(12));
        gl.EnableVertexAttribArray(1);
        // layout 2 = uv (2 floats), offset 24, stride 48
        gl.VertexAttribPointer(2, 2, GL_FLOAT, 0, 48, new IntPtr(24));
        gl.EnableVertexAttribArray(2);
        // layout 3 = tangent (4 floats), offset 32, stride 48 - solid shader ignores it
        gl.VertexAttribPointer(3, 4, GL_FLOAT, 0, 48, new IntPtr(32));
        gl.EnableVertexAttribArray(3);

        gl.BindVertexArray(0);

        InitGroundGridResources(gl, vsPreamble, fsPreamble);
        InitMarkersResources(gl, vsPreamble, fsPreamble);
        InitGizmoResources(gl, vsPreamble, fsPreamble);

        _glInitialized = true;
    }

    unsafe void InitGroundGridResources(GlInterface gl, string vsPreamble, string fsPreamble)
    {
        _gridProgram = CreateProgram(gl,
            vsPreamble + GridVertexShaderBody,
            fsPreamble + GridFragmentShaderBody);
        _uGridMvp   = gl.GetUniformLocationString(_gridProgram, "uMVP");
        _uGridColor = gl.GetUniformLocationString(_gridProgram, "uColor");

        // Build a 20x20 grid of XZ lines centered at origin in the y=0 plane.
        // Each line runs full extent in X or Z; lines spaced 1 unit apart.
        const int half = 10;            // lines from -half..+half
        const float step = 1.0f;
        const float extent = half * step;
        int lineCount = (half * 2 + 1) * 2; // X-direction + Z-direction
        int floatCount = lineCount * 2 * 3; // 2 verts per line, 3 floats per vert
        var verts = new float[floatCount];
        int vi = 0;
        for (int i = -half; i <= half; i++)
        {
            // Line parallel to X axis at Z = i*step
            float z = i * step;
            verts[vi++] = -extent; verts[vi++] = 0; verts[vi++] = z;
            verts[vi++] =  extent; verts[vi++] = 0; verts[vi++] = z;
            // Line parallel to Z axis at X = i*step
            float x = i * step;
            verts[vi++] = x; verts[vi++] = 0; verts[vi++] = -extent;
            verts[vi++] = x; verts[vi++] = 0; verts[vi++] =  extent;
        }
        _gridVertexCount = lineCount * 2;

        _gridVao = gl.GenVertexArray();
        gl.BindVertexArray(_gridVao);
        _gridVbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, _gridVbo);
        fixed (float* p = verts)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(verts.Length * sizeof(float)), (IntPtr)p, GL_STATIC_DRAW);
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 12, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.BindVertexArray(0);
    }

    unsafe void InitGizmoResources(GlInterface gl, string vsPreamble, string fsPreamble)
    {
        _gizmoProgram = CreateProgram(gl,
            vsPreamble + GizmoVertexShaderBody,
            fsPreamble + GizmoFragmentShaderBody);
        _uGizmoView  = gl.GetUniformLocationString(_gizmoProgram, "uViewRot");
        _uGizmoColor = gl.GetUniformLocationString(_gizmoProgram, "uColor");

        // 6 axis lines (origin to +-X/Y/Z), each as 2 vertices = 12 vertices.
        // Pack as one buffer so a single draw call covers it; we set color uniforms per-pair.
        var axes = new float[]
        {
            0,0,0,  1,0,0,
            0,0,0, -1,0,0,
            0,0,0,  0,1,0,
            0,0,0,  0,-1,0,
            0,0,0,  0,0,1,
            0,0,0,  0,0,-1,
        };
        _gizmoVertexCount = axes.Length / 3;

        _gizmoVao = gl.GenVertexArray();
        gl.BindVertexArray(_gizmoVao);
        _gizmoVbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, _gizmoVbo);
        fixed (float* p = axes)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(axes.Length * sizeof(float)), (IntPtr)p, GL_STATIC_DRAW);

        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 12, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.BindVertexArray(0);
    }

    void InitMarkersResources(GlInterface gl, string vsPreamble, string fsPreamble)
    {
        _markersProgram = CreateProgram(gl,
            vsPreamble + MarkersVertexShaderBody,
            fsPreamble + MarkersFragmentShaderBody);
        _uMarkersMvp   = gl.GetUniformLocationString(_markersProgram, "uMVP");
        _uMarkersColor = gl.GetUniformLocationString(_markersProgram, "uColor");

        _markersVao = gl.GenVertexArray();
        gl.BindVertexArray(_markersVao);
        _markersVbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, _markersVbo);
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 12, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.BindVertexArray(0);
        _markersVertexCapacity = 0;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (!_glInitialized) return;

        gl.BindBuffer(GL_ARRAY_BUFFER, 0);
        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);

        gl.DeleteBuffer(_vbo);
        gl.DeleteBuffer(_ebo);
        gl.DeleteVertexArray(_vao);
        gl.DeleteProgram(_program);

        gl.DeleteBuffer(_gridVbo);
        gl.DeleteVertexArray(_gridVao);
        gl.DeleteProgram(_gridProgram);

        gl.DeleteBuffer(_markersVbo);
        gl.DeleteVertexArray(_markersVao);
        gl.DeleteProgram(_markersProgram);

        gl.DeleteBuffer(_gizmoVbo);
        gl.DeleteVertexArray(_gizmoVao);
        gl.DeleteProgram(_gizmoProgram);

        _glInitialized = false;
        _meshDirty = true; // force re-upload when reattached
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        // Bind Avalonia's framebuffer
        gl.BindFramebuffer(GL_FRAMEBUFFER, fb);

        // Viewport with DPI scaling
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = (int)(Bounds.Width * scaling);
        int h = (int)(Bounds.Height * scaling);
        if (w <= 0 || h <= 0) return;

        UpdateGizmoAnimation();

        gl.Viewport(0, 0, w, h);

        gl.ClearColor(0.04f, 0.04f, 0.04f, 1f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        // Compute matrices early so the grid pass (which runs before the mesh) can use them.
        // GetViewMatrix returns eye position to avoid recomputing for the light direction.
        float aspect = (float)w / h;
        var view = _camera.GetViewMatrix(out var eye);
        var proj = _camera.GetProjectionMatrix(aspect);
        var mvp = view * proj;

        // Grid draws first so the mesh occludes it.
        DrawGroundGrid(gl, mvp);

        var mesh = _meshData;
        if (mesh == null) return;

        gl.Enable(GL_DEPTH_TEST);

        gl.BindVertexArray(_vao);

        // Upload mesh data if dirty
        if (_meshDirty)
        {
            _meshDirty = false;
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            fixed (float* ptr = mesh.Vertices)
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(mesh.Vertices.Length * sizeof(float)), (IntPtr)ptr, GL_STATIC_DRAW);

            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo);
            fixed (uint* ptr = mesh.Indices)
                gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)(mesh.Indices.Length * sizeof(uint)), (IntPtr)ptr, GL_STATIC_DRAW);
        }

        gl.UseProgram(_program);

        // System.Numerics is row-major, row-vector: v' = v * MVP
        // GLSL is column-major, column-vector: v' = MVP * v
        // Passing row-major data to glUniformMatrix4fv(transpose=false) reinterprets rows as
        // columns, which is the exact transpose needed for the convention switch.
        gl.UniformMatrix4fv(_uMvp, 1, false, &mvp.M11);

        // Light direction follows camera so the visible side is always well-lit
        if (_glUniform3f != null)
        {
            var target = new Vector3(_camera.TargetX, _camera.TargetY, _camera.TargetZ);
            var lightDir = Vector3.Normalize(eye - target);
            _glUniform3f(_uLightDir, lightDir.X, lightDir.Y, lightDir.Z);
            _glUniform3f(_uColor, 0.75f, 0.75f, 0.75f);
        }

        // Draw all mesh groups
        foreach (var (offset, count) in mesh.DrawGroups)
        {
            gl.DrawElements(GL_TRIANGLES, count, 0x1405 /* GL_UNSIGNED_INT */, (IntPtr)(offset * sizeof(uint)));
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GL_DEPTH_TEST);

        DrawMarkers(gl, mvp);
        ProjectAndEmitMarkers(mvp, scaling, w, h);
        DrawGizmo(gl, w, h, scaling);
        ProjectAndEmitGizmoLabels(scaling, w, h);
    }

    unsafe void DrawGroundGrid(GlInterface gl, in Matrix4x4 mvp)
    {
        if (!_showGroundGrid) return;

        gl.UseProgram(_gridProgram);
        var mvpCopy = mvp;
        gl.UniformMatrix4fv(_uGridMvp, 1, false, &mvpCopy.M11);

        // Solid dim gray; the mesh occludes it via depth testing.
        if (_glUniform4f != null)
            _glUniform4f(_uGridColor, 0.20f, 0.20f, 0.20f, 1.0f);

        // Render lines under the mesh: enable depth test, write depth so the mesh occludes.
        gl.Enable(GL_DEPTH_TEST);
        gl.DepthMask(1);

        gl.BindVertexArray(_gridVao);
        gl.DrawArrays(0x0001 /* GL_LINES */, 0, _gridVertexCount);
        gl.BindVertexArray(0);

        gl.UseProgram(0);
    }

    unsafe void DrawMarkers(GlInterface gl, in Matrix4x4 mvp)
    {
        var mesh = _meshData;
        if (mesh == null) return;
        if (mesh.Attachments.Length == 0 && mesh.ImpactPoints.Length == 0) return;
        if (!_showMarkers) return;

        float markerSize = MathF.Max(mesh.Radius * 0.04f, 0.05f);
        float impactSize = markerSize * 0.5f;

        // Build vertex list: 6 vertices per attachment (3 segments) + 6 per impact (3 stub axes).
        int totalLines = mesh.Attachments.Length * 3 + mesh.ImpactPoints.Length * 3;
        int totalFloats = totalLines * 2 * 3;

        Span<float> verts = stackalloc float[Math.Min(totalFloats, 4096)];
        float[]? heap = null;
        if (totalFloats > verts.Length)
        {
            heap = System.Buffers.ArrayPool<float>.Shared.Rent(totalFloats);
            verts = heap.AsSpan(0, totalFloats);
        }

        int vi = 0;
        // Inline segment writes; cannot use a local function because Span captures aren't allowed.
        int attLineCount = mesh.Attachments.Length * 3;
        foreach (var m in mesh.Attachments)
        {
            var p = m.Position;
            var ex = p + m.AxisX * markerSize;
            var ey = p + m.AxisY * markerSize;
            var ez = p + m.AxisZ * markerSize;
            verts[vi++] = p.X; verts[vi++] = p.Y; verts[vi++] = p.Z;
            verts[vi++] = ex.X; verts[vi++] = ex.Y; verts[vi++] = ex.Z;
            verts[vi++] = p.X; verts[vi++] = p.Y; verts[vi++] = p.Z;
            verts[vi++] = ey.X; verts[vi++] = ey.Y; verts[vi++] = ey.Z;
            verts[vi++] = p.X; verts[vi++] = p.Y; verts[vi++] = p.Z;
            verts[vi++] = ez.X; verts[vi++] = ez.Y; verts[vi++] = ez.Z;
        }
        foreach (var m in mesh.ImpactPoints)
        {
            var p = m.Position;
            var ex = p + m.AxisX * impactSize;
            var ey = p + m.AxisY * impactSize;
            var ez = p + m.AxisZ * impactSize;
            verts[vi++] = p.X; verts[vi++] = p.Y; verts[vi++] = p.Z;
            verts[vi++] = ex.X; verts[vi++] = ex.Y; verts[vi++] = ex.Z;
            verts[vi++] = p.X; verts[vi++] = p.Y; verts[vi++] = p.Z;
            verts[vi++] = ey.X; verts[vi++] = ey.Y; verts[vi++] = ey.Z;
            verts[vi++] = p.X; verts[vi++] = p.Y; verts[vi++] = p.Z;
            verts[vi++] = ez.X; verts[vi++] = ez.Y; verts[vi++] = ez.Z;
        }

        gl.BindVertexArray(_markersVao);
        gl.BindBuffer(GL_ARRAY_BUFFER, _markersVbo);
        fixed (float* p = verts)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(totalFloats * sizeof(float)), (IntPtr)p, 0x88E8 /* GL_DYNAMIC_DRAW */);

        gl.UseProgram(_markersProgram);
        var mvpCopy = mvp;
        gl.UniformMatrix4fv(_uMarkersMvp, 1, false, &mvpCopy.M11);

        gl.Enable(GL_DEPTH_TEST);
        gl.DepthMask(0);

        if (_glUniform3f != null)
        {
            // Attachments: red / green / blue per axis. Impact points: same colors but dimmer.
            for (int i = 0; i < mesh.Attachments.Length; i++)
            {
                int baseV = i * 6;
                _glUniform3f(_uMarkersColor, 1.0f, 0.20f, 0.20f); gl.DrawArrays(0x0001 /* GL_LINES */, baseV + 0, 2);
                _glUniform3f(_uMarkersColor, 0.20f, 1.0f, 0.20f); gl.DrawArrays(0x0001 /* GL_LINES */, baseV + 2, 2);
                _glUniform3f(_uMarkersColor, 0.30f, 0.50f, 1.0f); gl.DrawArrays(0x0001 /* GL_LINES */, baseV + 4, 2);
            }
            int impactBase = attLineCount * 2;
            for (int i = 0; i < mesh.ImpactPoints.Length; i++)
            {
                int baseV = impactBase + i * 6;
                _glUniform3f(_uMarkersColor, 0.7f, 0.4f, 0.4f); gl.DrawArrays(0x0001 /* GL_LINES */, baseV + 0, 2);
                _glUniform3f(_uMarkersColor, 0.4f, 0.7f, 0.4f); gl.DrawArrays(0x0001 /* GL_LINES */, baseV + 2, 2);
                _glUniform3f(_uMarkersColor, 0.4f, 0.5f, 0.7f); gl.DrawArrays(0x0001 /* GL_LINES */, baseV + 4, 2);
            }
        }

        gl.DepthMask(1);
        gl.Disable(GL_DEPTH_TEST);
        gl.BindVertexArray(0);
        gl.UseProgram(0);

        if (heap != null) System.Buffers.ArrayPool<float>.Shared.Return(heap);
    }

    void ProjectAndEmitGizmoLabels(double scaling, int viewportW, int viewportH)
    {
        if (GizmoLabelsProjected == null) return;
        _gizmoLabelBuffer.Clear();

        // Logical pixels (Avalonia coords). Mirrors the math in HitTestGizmo.
        double size   = GizmoSizePx;
        double margin = GizmoMarginPx;
        double cx = (viewportW / scaling) - size * 0.5 - margin;
        double cy = margin + size * 0.5;
        double r  = size * 0.5 - 4;

        var rot = GetCameraViewRotation();

        // Only +X / +Y / +Z get filled circular letter markers.
        // Indices match _hoveredGizmoAxis ordering: 0=+X, 2=+Y, 4=+Z.
        var posAxes = new (int axis, string letter, float ax, float ay, float az)[]
        {
            (0, "X", 1, 0, 0),
            (2, "Y", 0, 1, 0),
            (4, "Z", 0, 0, 1),
        };
        foreach (var (axisIdx, letter, ax, ay, az) in posAxes)
        {
            float vx = ax * rot.M11 + ay * rot.M21 + az * rot.M31;
            float vy = ax * rot.M12 + ay * rot.M22 + az * rot.M32;
            double sx = cx + vx * r;
            double sy = cy - vy * r;
            bool hovered = _hoveredGizmoAxis == axisIdx;
            _gizmoLabelBuffer.Add(new GizmoLabel(axisIdx, letter, sx, sy, hovered));
        }
        GizmoLabelsProjected.Invoke(_gizmoLabelBuffer);
    }

    void ProjectAndEmitMarkers(in System.Numerics.Matrix4x4 mvp, double scaling, int viewportW, int viewportH)
    {
        if (MarkersProjected == null) return;
        var mesh = _meshData;
        _markerLabelBuffer.Clear();
        if (mesh == null || !_showMarkers)
        {
            MarkersProjected.Invoke(_markerLabelBuffer);
            return;
        }

        // Inline projection; local function cannot capture an `in` parameter.
        foreach (var m in mesh.Attachments)
        {
            var p = new System.Numerics.Vector4(m.Position, 1.0f);
            var clip = System.Numerics.Vector4.Transform(p, mvp);
            if (clip.W <= 0) { _markerLabelBuffer.Add(new MarkerLabel(m.Name, 0, 0, false)); continue; }
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            bool inside = ndcX >= -1 && ndcX <= 1 && ndcY >= -1 && ndcY <= 1;
            double sx = (ndcX * 0.5 + 0.5) * (viewportW / scaling);
            double sy = (1.0 - (ndcY * 0.5 + 0.5)) * (viewportH / scaling);
            _markerLabelBuffer.Add(new MarkerLabel(m.Name, sx, sy, inside));
        }
        foreach (var m in mesh.ImpactPoints)
        {
            var p = new System.Numerics.Vector4(m.Position, 1.0f);
            var clip = System.Numerics.Vector4.Transform(p, mvp);
            if (clip.W <= 0) { _markerLabelBuffer.Add(new MarkerLabel(m.Name, 0, 0, false)); continue; }
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            bool inside = ndcX >= -1 && ndcX <= 1 && ndcY >= -1 && ndcY <= 1;
            double sx = (ndcX * 0.5 + 0.5) * (viewportW / scaling);
            double sy = (1.0 - (ndcY * 0.5 + 0.5)) * (viewportH / scaling);
            _markerLabelBuffer.Add(new MarkerLabel(m.Name, sx, sy, inside));
        }
        MarkersProjected.Invoke(_markerLabelBuffer);
    }

    Matrix4x4 GetCameraViewRotation()
    {
        // Strip translation from the view matrix to get pure rotation.
        var view = _camera.GetViewMatrix();
        view.M41 = 0; view.M42 = 0; view.M43 = 0;
        return view;
    }

    unsafe void DrawGizmo(GlInterface gl, int viewportW, int viewportH, double scaling)
    {
        int gizmoSize = (int)(GizmoSizePx * scaling);
        int margin    = (int)(GizmoMarginPx * scaling);
        int gx = viewportW - gizmoSize - margin;
        int gy = viewportH - gizmoSize - margin;

        gl.Viewport(gx, gy, gizmoSize, gizmoSize);
        gl.Clear(GL_DEPTH_BUFFER_BIT);
        gl.Disable(GL_DEPTH_TEST);

        gl.UseProgram(_gizmoProgram);

        var rot = GetCameraViewRotation();
        // Pass the upper 3x3 of the view rotation as a mat3.
        float[] m3 =
        {
            rot.M11, rot.M12, rot.M13,
            rot.M21, rot.M22, rot.M23,
            rot.M31, rot.M32, rot.M33
        };
        if (_glUniformMatrix3fv != null)
        {
            fixed (float* mp = m3)
                _glUniformMatrix3fv(_uGizmoView, 1, 0, mp);
        }

        gl.BindVertexArray(_gizmoVao);

        // Six axis colors: +X, -X, +Y, -Y, +Z, -Z
        // Hovered axis is brightened by 1.3x, clamped per-channel.
        var colors = new (float r, float g, float b)[]
        {
            (1.0f, 0.20f, 0.20f), (0.5f, 0.10f, 0.10f),
            (0.20f, 1.0f, 0.20f), (0.10f, 0.5f, 0.10f),
            (0.30f, 0.50f, 1.0f), (0.15f, 0.25f, 0.5f),
        };

        if (_glLineWidth != null) _glLineWidth(2.0f);
        if (_glUniform3f != null)
        {
            for (int i = 0; i < 6; i++)
            {
                var c = colors[i];
                if (i == _hoveredGizmoAxis)
                {
                    c.r = MathF.Min(1.0f, c.r * 1.3f);
                    c.g = MathF.Min(1.0f, c.g * 1.3f);
                    c.b = MathF.Min(1.0f, c.b * 1.3f);
                }
                _glUniform3f(_uGizmoColor, c.r, c.g, c.b);
                gl.DrawArrays(0x0001 /* GL_LINES */, i * 2, 2);
            }
        }
        if (_glLineWidth != null) _glLineWidth(1.0f);

        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    int HitTestGizmo(Point screenPos)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return -1;

        double margin = GizmoMarginPx;
        double size   = GizmoSizePx;
        // Gizmo occupies a square in the top-right. In Avalonia coords, top-left is (0,0).
        double left = w - size - margin;
        double top  = margin;
        if (screenPos.X < left || screenPos.X > left + size) return -1;
        if (screenPos.Y < top  || screenPos.Y > top  + size) return -1;

        // Project the six axis end points into the gizmo's local screen space.
        // Local space: x right, y up, origin at gizmo center.
        var rot = GetCameraViewRotation();
        var axisLocal = new (float x, float y, float z)[]
        {
            ( 1, 0, 0), (-1, 0, 0),
            ( 0, 1, 0), ( 0,-1, 0),
            ( 0, 0, 1), ( 0, 0,-1),
        };

        double cx = left + size * 0.5;
        double cy = top  + size * 0.5;
        double r  = size * 0.5 - 4;

        int best = -1;
        double bestDistSq = 18 * 18; // 18 px hit radius - widened so axis lines are easier to click

        for (int i = 0; i < 6; i++)
        {
            var a = axisLocal[i];
            // (rot * a) using the 3x3 rotation: row-vector * row-major matrix
            float vx = a.x * rot.M11 + a.y * rot.M21 + a.z * rot.M31;
            float vy = a.x * rot.M12 + a.y * rot.M22 + a.z * rot.M32;
            // Clip y is flipped to screen y in Avalonia.
            double sx = cx + vx * r;
            double sy = cy - vy * r;
            double dx = screenPos.X - sx;
            double dy = screenPos.Y - sy;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                best = i;
            }
        }
        return best;
    }

    static (float Azimuth, float Elevation) GetGizmoTarget(int axis) => axis switch
    {
        // Azimuth = 0 looks down -Z (camera at +Z). Each axis-end click positions
        // the camera at that axis end, looking toward the origin.
        0 => (90f, 0f),    // +X
        1 => (-90f, 0f),   // -X
        2 => (0f, 89f),    // +Y (clamped to elevation limit)
        3 => (0f, -89f),   // -Y
        4 => (0f, 0f),     // +Z
        5 => (180f, 0f),   // -Z
        _ => (0f, 0f)
    };

    static float ShortestAzimuthDelta(float from, float to)
    {
        float d = (to - from) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }

    void StartGizmoTween(int axis)
    {
        var (targetAz, targetEl) = GetGizmoTarget(axis);
        _animStartAz = _camera.Azimuth;
        _animStartEl = _camera.Elevation;
        _animEndAz   = _animStartAz + ShortestAzimuthDelta(_animStartAz, targetAz);
        _animEndEl   = targetEl;
        if (!_animClock.IsRunning) _animClock.Start();
        _animStartTimeMs = _animClock.Elapsed.TotalMilliseconds;
        _animActive = true;
        RequestNextFrameRendering();
    }

    void UpdateGizmoAnimation()
    {
        if (!_animActive) return;
        double now = _animClock.Elapsed.TotalMilliseconds;
        double t = (now - _animStartTimeMs) / AnimDurationMs;
        if (t >= 1.0)
        {
            _camera.Azimuth = _animEndAz;
            _camera.Elevation = _animEndEl;
            _animActive = false;
            return;
        }
        // Ease in / out (quadratic)
        float eased = (float)(t < 0.5
            ? 2.0 * t * t
            : 1.0 - System.Math.Pow(-2.0 * t + 2.0, 2.0) / 2.0);
        _camera.Azimuth   = _animStartAz + (_animEndAz - _animStartAz) * eased;
        _camera.Elevation = _animStartEl + (_animEndEl - _animStartEl) * eased;
        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsLeftButtonPressed)
        {
            int axis = HitTestGizmo(pos);
            if (axis >= 0 && !_animActive)
            {
                StartGizmoTween(axis);
                e.Handled = true;
                return;
            }
        }

        _lastPointerPos = pos;
        if (props.IsLeftButtonPressed) _leftDragging = true;
        if (props.IsRightButtonPressed) _rightDragging = true;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) _leftDragging = false;
        if (!props.IsRightButtonPressed) _rightDragging = false;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (!_leftDragging && !_rightDragging)
        {
            int axis = _animActive ? -1 : HitTestGizmo(pos);
            if (axis != _hoveredGizmoAxis)
            {
                _hoveredGizmoAxis = axis;
                Cursor = axis >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                RequestNextFrameRendering();
            }
        }

        float dx = (float)(pos.X - _lastPointerPos.X);
        float dy = (float)(pos.Y - _lastPointerPos.Y);
        _lastPointerPos = pos;

        if (_leftDragging)
        {
            // Manual drag wins over animation
            _animActive = false;
            _camera.Rotate(-dx * 0.3f, dy * 0.3f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
        else if (_rightDragging)
        {
            _animActive = false;
            _camera.Pan(-dx * 0.003f, dy * 0.003f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Zoom((float)e.Delta.Y);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    #region GL Helpers

    static int CreateProgram(GlInterface gl, string vertexSrc, string fragmentSrc)
    {
        int vs = CompileShader(gl, GL_VERTEX_SHADER, vertexSrc);
        int fs = CompileShader(gl, GL_FRAGMENT_SHADER, fragmentSrc);

        int program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);

        // Delete shader objects (they stay attached until program is deleted)
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

    #endregion
}
